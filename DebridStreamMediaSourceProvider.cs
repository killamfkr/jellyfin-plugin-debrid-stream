using System.Globalization;
using System.IO;
using Jellyfin.Plugin.DebridStream.Clients;
using Jellyfin.Plugin.DebridStream.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream;

/// <summary>
/// Adds dynamic HTTP sources for movies and episodes using a Stremio stream addon + debrid.
/// Exposes one Jellyfin media source per addon stream so clients can pick a link (similar to link pickers in other apps).
/// </summary>
public sealed class DebridStreamMediaSourceProvider : IMediaSourceProvider
{
    private const string OpenTokenSeparator = ":";
    private readonly ILibraryManager _libraryManager;
    private readonly StremioAddonStreamClient _stremioClient;
    private readonly RealDebridClient _realDebridClient;
    private readonly TorBoxClient _torBoxClient;
    private readonly TmdbExternalIdResolver _tmdbResolver;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DebridStreamMediaSourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebridStreamMediaSourceProvider"/> class.
    /// </summary>
    public DebridStreamMediaSourceProvider(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILoggerFactory loggerFactory,
        ILogger<DebridStreamMediaSourceProvider> logger)
    {
        _libraryManager = libraryManager;
        _memoryCache = memoryCache;
        _stremioClient = new StremioAddonStreamClient(
            httpClientFactory,
            loggerFactory.CreateLogger<StremioAddonStreamClient>());
        _realDebridClient = new RealDebridClient(
            httpClientFactory,
            loggerFactory.CreateLogger<RealDebridClient>());
        _torBoxClient = new TorBoxClient(
            httpClientFactory,
            loggerFactory.CreateLogger<TorBoxClient>());
        _tmdbResolver = new TmdbExternalIdResolver(
            httpClientFactory,
            loggerFactory.CreateLogger<TmdbExternalIdResolver>());
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableDebridStreams)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(config.StreamAddonBaseUrl))
        {
            return [];
        }

        if (config.DebridBackend == 0 && string.IsNullOrWhiteSpace(config.RealDebridApiToken))
        {
            return [];
        }

        if (config.DebridBackend == 1 && string.IsNullOrWhiteSpace(config.TorBoxApiKey))
        {
            return [];
        }

        if (item is not Movie && item is not Episode)
        {
            return [];
        }

        var descriptor = await GetStremioDescriptorAsync(item, config, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            return [];
        }

        var (stremioType, stremioVideoId) = descriptor.Value;
        var max = Math.Clamp(config.MaxStreamCandidates, 1, 48);
        var cacheSeconds = Math.Clamp(config.StreamListCacheSeconds, 0, 3600);
        var cacheKey = $"debridstream:entries:{item.Id:N}:{config.StreamAddonBaseUrl.TrimEnd('/')}:{stremioType}:{stremioVideoId}:{max}";

        IReadOnlyList<StremioStreamEntry> entries;
        if (cacheSeconds > 0)
        {
            entries = await _memoryCache.GetOrCreateAsync(
                cacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheSeconds);
                    return await _stremioClient.GetStreamEntriesAsync(
                            config.StreamAddonBaseUrl,
                            stremioType,
                            stremioVideoId,
                            max,
                            cancellationToken)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false) ?? [];
        }
        else
        {
            entries = await _stremioClient.GetStreamEntriesAsync(
                config.StreamAddonBaseUrl,
                stremioType,
                stremioVideoId,
                max,
                cancellationToken).ConfigureAwait(false);
        }

        if (entries.Count == 0)
        {
            return [];
        }

        var list = new List<MediaSourceInfo>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            list.Add(new MediaSourceInfo
            {
                Id = FormattableString.Invariant($"debridstream-{item.Id:N}-{i}"),
                Name = e.DisplayName,
                Protocol = global::MediaBrowser.Model.MediaInfo.MediaProtocol.Http,
                Path = string.Empty,
                IsRemote = true,
                RequiresOpening = true,
                OpenToken = item.Id.ToString("N", CultureInfo.InvariantCulture) + OpenTokenSeparator + i.ToString(CultureInfo.InvariantCulture),
                BufferMs = 4000,
                SupportsDirectStream = true,
                SupportsDirectPlay = true,
                SupportsTranscoding = true,
                SupportsProbing = true,
                Type = MediaSourceType.Default,
                Container = "mp4",
                RunTimeTicks = item.RunTimeTicks,
                MediaStreams =
                [
                    new MediaStream
                    {
                        Type = MediaStreamType.Video,
                        Index = -1,
                        IsInterlaced = true
                    },
                    new MediaStream
                    {
                        Type = MediaStreamType.Audio,
                        Index = -1
                    }
                ]
            });
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<ILiveStream> OpenMediaSource(string openToken, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration
                     ?? throw new InvalidOperationException("Debrid stream plugin is not loaded.");

        var parsed = ParseOpenToken(openToken);
        if (parsed is null)
        {
            throw new ArgumentException("Invalid open token.", nameof(openToken));
        }

        var (itemId, streamIndex) = parsed.Value;

        var item = _libraryManager.GetItemById(itemId)
                   ?? throw new KeyNotFoundException("Library item not found: " + itemId);

        var descriptor = await GetStremioDescriptorAsync(item, config, cancellationToken).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Item has no usable IMDb/TMDB/TVDB mapping for stream lookup.");

        var (stremioType, stremioVideoId) = descriptor;
        var max = Math.Clamp(config.MaxStreamCandidates, 1, 48);

        var entries = await _stremioClient.GetStreamEntriesAsync(
            config.StreamAddonBaseUrl,
            stremioType,
            stremioVideoId,
            max,
            cancellationToken).ConfigureAwait(false);

        if (streamIndex < 0 || streamIndex >= entries.Count)
        {
            throw new InvalidOperationException("Stream index is out of range; refresh the item and pick a stream again.");
        }

        var candidate = entries[streamIndex].Url;

        string? playbackUrl = null;
        try
        {
            if (config.DebridBackend == 0)
            {
                playbackUrl = await _realDebridClient
                    .ResolvePlaybackUrlAsync(config.RealDebridApiToken, candidate, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (candidate.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                playbackUrl = await _torBoxClient
                    .ResolveMagnetAsync(config.TorBoxApiKey, candidate, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Debrid resolution failed for selected stream");
        }

        if (string.IsNullOrEmpty(playbackUrl))
        {
            throw new InvalidOperationException("Could not resolve the selected stream through the configured debrid service.");
        }

        string ext;
        try
        {
            ext = Path.GetExtension(new Uri(playbackUrl).AbsolutePath.Split('?')[0]);
        }
        catch (UriFormatException)
        {
            ext = string.Empty;
        }

        var container = string.IsNullOrEmpty(ext) ? "mp4" : ext.TrimStart('.').ToLowerInvariant();

        var mediaSource = new MediaSourceInfo
        {
            Id = FormattableString.Invariant($"debridstream-opened-{item.Id:N}-{streamIndex}"),
            Name = entries[streamIndex].DisplayName,
            Protocol = global::MediaBrowser.Model.MediaInfo.MediaProtocol.Http,
            Path = playbackUrl,
            IsRemote = true,
            RequiresOpening = false,
            BufferMs = 4000,
            SupportsDirectStream = true,
            SupportsDirectPlay = true,
            SupportsTranscoding = true,
            SupportsProbing = true,
            Type = MediaSourceType.Default,
            Container = container,
            RunTimeTicks = item.RunTimeTicks,
            MediaStreams =
            [
                new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Index = -1,
                    IsInterlaced = true
                },
                new MediaStream
                {
                    Type = MediaStreamType.Audio,
                    Index = -1
                }
            ]
        };

        mediaSource.LiveStreamId = mediaSource.Id;
        mediaSource.RequiresClosing = true;

        var liveStream = new DebridExclusiveLiveStream(mediaSource, () => Task.CompletedTask);
        await liveStream.Open(cancellationToken).ConfigureAwait(false);
        return liveStream;
    }

    private static (Guid ItemId, int Index)? ParseOpenToken(string openToken)
    {
        if (string.IsNullOrWhiteSpace(openToken))
        {
            return null;
        }

        var idx = openToken.LastIndexOf(OpenTokenSeparator, StringComparison.Ordinal);
        if (idx <= 0 || idx >= openToken.Length - 1)
        {
            if (Guid.TryParseExact(openToken, "N", out var legacyId))
            {
                return (legacyId, 0);
            }

            return null;
        }

        var idPart = openToken[..idx];
        var indexPart = openToken[(idx + 1)..];

        if (!Guid.TryParseExact(idPart, "N", out var itemId))
        {
            return null;
        }

        if (!int.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamIndex))
        {
            return null;
        }

        return (itemId, streamIndex);
    }

    private async Task<(string Type, string VideoId)?> GetStremioDescriptorAsync(
        BaseItem item,
        DebridStreamPluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var tmdbKey = string.IsNullOrWhiteSpace(config.TmdbApiKey) ? null : config.TmdbApiKey.Trim();

        if (item is Movie movie)
        {
            var imdb = await _tmdbResolver.ResolveMovieImdbIdAsync(movie.ProviderIds, tmdbKey, cancellationToken).ConfigureAwait(false);
            return imdb is null ? null : ("movie", imdb);
        }

        if (item is Episode ep)
        {
            var series = _libraryManager.GetItemById(ep.SeriesId) as Series;
            if (series is null)
            {
                return null;
            }

            var simdb = await _tmdbResolver.ResolveSeriesImdbIdAsync(series.ProviderIds, tmdbKey, cancellationToken).ConfigureAwait(false);
            if (simdb is null)
            {
                return null;
            }

            var season = ep.ParentIndexNumber ?? 1;
            var episode = ep.IndexNumber ?? 1;
            return ("series", FormattableString.Invariant($"{simdb}:{season}:{episode}"));
        }

        return null;
    }
}
