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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream;

/// <summary>
/// Adds dynamic HTTP sources for movies and episodes using a Stremio stream addon + debrid.
/// </summary>
public sealed class DebridStreamMediaSourceProvider : IMediaSourceProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly StremioAddonStreamClient _stremioClient;
    private readonly RealDebridClient _realDebridClient;
    private readonly TorBoxClient _torBoxClient;
    private readonly ILogger<DebridStreamMediaSourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebridStreamMediaSourceProvider"/> class.
    /// </summary>
    public DebridStreamMediaSourceProvider(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<DebridStreamMediaSourceProvider> logger)
    {
        _libraryManager = libraryManager;
        _stremioClient = new StremioAddonStreamClient(
            httpClientFactory,
            loggerFactory.CreateLogger<StremioAddonStreamClient>());
        _realDebridClient = new RealDebridClient(
            httpClientFactory,
            loggerFactory.CreateLogger<RealDebridClient>());
        _torBoxClient = new TorBoxClient(
            httpClientFactory,
            loggerFactory.CreateLogger<TorBoxClient>());
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableDebridStreams)
        {
            return Task.FromResult<IEnumerable<MediaSourceInfo>>([]);
        }

        if (string.IsNullOrWhiteSpace(config.StreamAddonBaseUrl))
        {
            return Task.FromResult<IEnumerable<MediaSourceInfo>>([]);
        }

        if (config.DebridBackend == 0 && string.IsNullOrWhiteSpace(config.RealDebridApiToken))
        {
            return Task.FromResult<IEnumerable<MediaSourceInfo>>([]);
        }

        if (config.DebridBackend == 1 && string.IsNullOrWhiteSpace(config.TorBoxApiKey))
        {
            return Task.FromResult<IEnumerable<MediaSourceInfo>>([]);
        }

        if (item is not Movie && item is not Episode)
        {
            return Task.FromResult<IEnumerable<MediaSourceInfo>>([]);
        }

        if (GetStremioDescriptor(item) is null)
        {
            return Task.FromResult<IEnumerable<MediaSourceInfo>>([]);
        }

        var source = new MediaSourceInfo
        {
            Id = "debridstream-" + item.Id.ToString("N", CultureInfo.InvariantCulture),
            Name = "Debrid / Stremio stream",
            Protocol = MediaProtocol.Http,
            Path = string.Empty,
            IsRemote = true,
            RequiresOpening = true,
            OpenToken = item.Id.ToString("N", CultureInfo.InvariantCulture),
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
        };

        return Task.FromResult<IEnumerable<MediaSourceInfo>>([source]);
    }

    /// <inheritdoc />
    public async Task<ILiveStream> OpenMediaSource(string openToken, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration
                     ?? throw new InvalidOperationException("Debrid stream plugin is not loaded.");

        if (!Guid.TryParse(openToken, out var itemId))
        {
            throw new ArgumentException("Invalid open token.", nameof(openToken));
        }

        var item = _libraryManager.GetItemById(itemId)
                   ?? throw new KeyNotFoundException("Library item not found: " + itemId);

        var descriptor = GetStremioDescriptor(item)
                         ?? throw new InvalidOperationException("Item has no IMDb id for Stremio stream lookup.");

        var candidates = await _stremioClient.GetStreamUrlsAsync(
            config.StreamAddonBaseUrl,
            descriptor.Value.Type,
            descriptor.Value.VideoId,
            Math.Clamp(config.MaxStreamCandidates, 1, 24),
            cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("The stream addon returned no playable candidates for this title.");
        }

        string? playbackUrl = null;
        foreach (var candidate in candidates)
        {
            try
            {
                if (config.DebridBackend == 0)
                {
                    playbackUrl = await _realDebridClient
                        .ResolvePlaybackUrlAsync(config.RealDebridApiToken, candidate, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    if (candidate.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    {
                        playbackUrl = await _torBoxClient
                            .ResolveMagnetAsync(config.TorBoxApiKey, candidate, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (!string.IsNullOrEmpty(playbackUrl))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Debrid candidate failed");
            }
        }

        if (string.IsNullOrEmpty(playbackUrl))
        {
            throw new InvalidOperationException("Could not resolve any stream through the configured debrid service.");
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
            Id = "debridstream-opened-" + item.Id.ToString("N", CultureInfo.InvariantCulture),
            Name = "Debrid / Stremio stream",
            Protocol = MediaProtocol.Http,
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

    private (string Type, string VideoId)? GetStremioDescriptor(BaseItem item)
    {
        if (item is Movie movie)
        {
            var imdb = GetImdbId(movie.ProviderIds);
            return imdb is null ? null : ("movie", imdb);
        }

        if (item is Episode ep)
        {
            var series = _libraryManager.GetItemById(ep.SeriesId) as Series;
            if (series is null)
            {
                return null;
            }

            var simdb = GetImdbId(series.ProviderIds);
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

    private static string? GetImdbId(Dictionary<string, string> providerIds)
    {
        if (!providerIds.TryGetValue(MetadataProvider.Imdb.ToString(), out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return "tt" + raw;
    }
}
