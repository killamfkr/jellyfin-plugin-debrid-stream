using System.IO;
using System.Net.Mime;
using Jellyfin.Plugin.DebridStream.Clients;
using Jellyfin.Plugin.DebridStream.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream.Api;

/// <summary>
/// Authenticated REST API for TMDB-backed stream discovery (no library items required).
/// </summary>
[ApiController]
[Authorize]
[Route("[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class DebridStreamDiscoveryController : ControllerBase
{
    private readonly TmdbDiscoverClient _tmdbDiscover;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebridStreamDiscoveryController"/> class.
    /// </summary>
    public DebridStreamDiscoveryController(
        TmdbDiscoverClient tmdbDiscover,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _tmdbDiscover = tmdbDiscover;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Reports whether discovery is configured.
    /// </summary>
    /// <returns>Status DTO.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DiscoveryStatusDto> GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return Ok(new DiscoveryStatusDto
            {
                Ready = false,
                Message = "Plugin is not loaded."
            });
        }

        var hasTmdb = !string.IsNullOrWhiteSpace(config.TmdbApiKey);
        var hasAddon = !string.IsNullOrWhiteSpace(config.StreamAddonBaseUrl);
        var hasDebrid = config.DebridBackend == 0
            ? !string.IsNullOrWhiteSpace(config.RealDebridApiToken)
            : !string.IsNullOrWhiteSpace(config.TorBoxApiKey);

        var ready = hasTmdb && config.EnableDebridStreams && hasAddon && hasDebrid;
        string? message = null;
        if (!hasTmdb)
        {
            message = "Set a TMDB API key in the plugin settings (required for discovery).";
        }
        else if (!config.EnableDebridStreams)
        {
            message = "Enable debrid streams in the plugin settings.";
        }
        else if (!hasAddon)
        {
            message = "Set a stream addon base URL.";
        }
        else if (!hasDebrid)
        {
            message = "Set Real-Debrid or TorBox credentials.";
        }

        return Ok(new DiscoveryStatusDto
        {
            Ready = ready,
            Message = message,
            HasTmdbKey = hasTmdb,
            StreamsEnabled = config.EnableDebridStreams,
            HasStreamAddon = hasAddon,
            HasDebrid = hasDebrid
        });
    }

    /// <summary>
    /// Search TMDB (movies and TV).
    /// </summary>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<DiscoveryTitleDto>>> Search(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var key = GetTmdbKeyOrBadRequest(out var bad);
        if (key is null)
        {
            return bad!;
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query is required.");
        }

        page = Math.Max(1, page);
        var results = await _tmdbDiscover.SearchAsync(key, q.Trim(), page, cancellationToken).ConfigureAwait(false);
        return Ok(results);
    }

    /// <summary>
    /// Trending movies and TV.
    /// </summary>
    [HttpGet("Trending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DiscoveryTrendingDto>> Trending(
        [FromQuery] string timeWindow = "day",
        CancellationToken cancellationToken = default)
    {
        var key = GetTmdbKeyOrBadRequest(out var bad);
        if (key is null)
        {
            return bad!;
        }

        var (movies, tv) = await _tmdbDiscover.GetTrendingAsync(key, timeWindow, cancellationToken).ConfigureAwait(false);
        return Ok(new DiscoveryTrendingDto { Movies = movies, Tv = tv });
    }

    /// <summary>
    /// Stream list for a movie (TMDB id).
    /// </summary>
    [HttpGet("Movie/{tmdbId:int}/Streams")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DiscoveryStreamLineDto>>> MovieStreams(
        int tmdbId,
        CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableDebridStreams || string.IsNullOrWhiteSpace(config.StreamAddonBaseUrl))
        {
            return BadRequest("Stream addon is not configured or streams are disabled.");
        }

        var key = GetTmdbKeyOrBadRequest(out var bad);
        if (key is null)
        {
            return bad!;
        }

        var imdb = await _tmdbDiscover.GetMovieImdbIdAsync(key, tmdbId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(imdb))
        {
            return NotFound("No IMDb id for this movie on TMDB.");
        }

        var stremio = new StremioAddonStreamClient(
            _httpClientFactory,
            _loggerFactory.CreateLogger<StremioAddonStreamClient>());
        var max = Math.Clamp(config.MaxStreamCandidates, 1, 48);
        var entries = await stremio.GetStreamEntriesAsync(
            config.StreamAddonBaseUrl,
            "movie",
            imdb,
            max,
            cancellationToken).ConfigureAwait(false);

        return Ok(ToStreamDtos(entries));
    }

    /// <summary>
    /// TV seasons.
    /// </summary>
    [HttpGet("Tv/{tmdbTvId:int}/Seasons")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DiscoverySeasonDto>>> TvSeasons(
        int tmdbTvId,
        CancellationToken cancellationToken = default)
    {
        var key = GetTmdbKeyOrBadRequest(out var bad);
        if (key is null)
        {
            return bad!;
        }

        var seasons = await _tmdbDiscover.GetTvSeasonsAsync(key, tmdbTvId, cancellationToken).ConfigureAwait(false);
        return Ok(seasons);
    }

    /// <summary>
    /// Episodes in a season.
    /// </summary>
    [HttpGet("Tv/{tmdbTvId:int}/Season/{seasonNumber:int}/Episodes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DiscoveryEpisodeDto>>> TvEpisodes(
        int tmdbTvId,
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        var key = GetTmdbKeyOrBadRequest(out var bad);
        if (key is null)
        {
            return bad!;
        }

        var episodes = await _tmdbDiscover.GetTvEpisodesAsync(key, tmdbTvId, seasonNumber, cancellationToken).ConfigureAwait(false);
        return Ok(episodes);
    }

    /// <summary>
    /// Stream list for a TV episode.
    /// </summary>
    [HttpGet("Tv/{tmdbTvId:int}/Streams")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DiscoveryStreamLineDto>>> TvEpisodeStreams(
        int tmdbTvId,
        [FromQuery] int season,
        [FromQuery] int episode,
        CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableDebridStreams || string.IsNullOrWhiteSpace(config.StreamAddonBaseUrl))
        {
            return BadRequest("Stream addon is not configured or streams are disabled.");
        }

        var key = GetTmdbKeyOrBadRequest(out var bad);
        if (key is null)
        {
            return bad!;
        }

        var imdb = await _tmdbDiscover.GetTvImdbIdAsync(key, tmdbTvId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(imdb))
        {
            return NotFound("No IMDb id for this series on TMDB.");
        }

        var stremioId = $"{imdb}:{season.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{episode.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var stremio = new StremioAddonStreamClient(
            _httpClientFactory,
            _loggerFactory.CreateLogger<StremioAddonStreamClient>());
        var max = Math.Clamp(config.MaxStreamCandidates, 1, 48);
        var entries = await stremio.GetStreamEntriesAsync(
            config.StreamAddonBaseUrl,
            "series",
            stremioId,
            max,
            cancellationToken).ConfigureAwait(false);

        return Ok(ToStreamDtos(entries));
    }

    /// <summary>
    /// Resolve a stream URL through the configured debrid backend.
    /// </summary>
    [HttpPost("Resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DiscoveryResolveResponseDto>> Resolve(
        [FromBody] DiscoveryResolveRequestDto body,
        CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableDebridStreams)
        {
            return BadRequest("Plugin not available or streams disabled.");
        }

        if (string.IsNullOrWhiteSpace(body.Url))
        {
            return BadRequest("Url is required.");
        }

        var url = body.Url.Trim();
        string? playbackUrl = null;

        if (config.DebridBackend == 0)
        {
            if (string.IsNullOrWhiteSpace(config.RealDebridApiToken))
            {
                return BadRequest("Real-Debrid token not configured.");
            }

            var rd = new RealDebridClient(
                _httpClientFactory,
                _loggerFactory.CreateLogger<RealDebridClient>());
            playbackUrl = await rd.ResolvePlaybackUrlAsync(config.RealDebridApiToken, url, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.TorBoxApiKey))
            {
                return BadRequest("TorBox key not configured.");
            }

            if (!url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("TorBox backend only supports magnet links from the addon.");
            }

            var tb = new TorBoxClient(
                _httpClientFactory,
                _loggerFactory.CreateLogger<TorBoxClient>());
            playbackUrl = await tb.ResolveMagnetAsync(config.TorBoxApiKey, url, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(playbackUrl))
        {
            return BadRequest("Could not resolve playback URL.");
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

        return Ok(new DiscoveryResolveResponseDto
        {
            PlaybackUrl = playbackUrl,
            Container = container
        });
    }

    private string? GetTmdbKeyOrBadRequest(out ActionResult? badRequest)
    {
        badRequest = null;
        var config = Plugin.Instance?.Configuration;
        var key = config?.TmdbApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            badRequest = BadRequest("TMDB API key is not configured.");
            return null;
        }

        return key;
    }

    private static IReadOnlyList<DiscoveryStreamLineDto> ToStreamDtos(IReadOnlyList<StremioStreamEntry> entries)
    {
        var list = new List<DiscoveryStreamLineDto>(entries.Count);
        foreach (var e in entries)
        {
            list.Add(new DiscoveryStreamLineDto
            {
                Name = e.DisplayName,
                Url = e.Url
            });
        }

        return list;
    }
}
