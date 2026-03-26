using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.DebridStream.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream.Clients;

/// <summary>
/// TMDB v3 HTTP client for discovery (search, trending, TV seasons).
/// </summary>
public sealed class TmdbDiscoverClient
{
    private const string ApiBase = "https://api.themoviedb.org/3/";
    private const string ImageBase = "https://image.tmdb.org/t/p/w342";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbDiscoverClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbDiscoverClient"/> class.
    /// </summary>
    public TmdbDiscoverClient(IHttpClientFactory httpClientFactory, ILogger<TmdbDiscoverClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets the poster URL for a path, or null.
    /// </summary>
    public static string? PosterUrl(string? posterPath)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        return ImageBase + posterPath.TrimStart('/');
    }

    /// <inheritdoc cref="SearchAsync"/>
    public async Task<IReadOnlyList<DiscoveryTitleDto>> SearchAsync(
        string apiKey,
        string query,
        int page,
        CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}search/multi?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}&page={page.ToString(CultureInfo.InvariantCulture)}";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return [];
        }

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<DiscoveryTitleDto>();
        foreach (var el in results.EnumerateArray())
        {
            if (!el.TryGetProperty("media_type", out var mt) || mt.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var mediaType = mt.GetString();
            if (mediaType is not ("movie" or "tv"))
            {
                continue;
            }

            if (!el.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var id = idProp.GetInt32();
            var title = mediaType == "movie"
                ? GetString(el, "title")
                : GetString(el, "name");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            list.Add(new DiscoveryTitleDto
            {
                MediaType = mediaType,
                TmdbId = id,
                Title = title.Trim(),
                Overview = GetString(el, "overview"),
                PosterUrl = PosterUrl(GetString(el, "poster_path")),
                ReleaseYear = ParseYear(mediaType == "movie" ? GetString(el, "release_date") : GetString(el, "first_air_date"))
            });
        }

        return list;
    }

    /// <summary>
    /// Trending movies and TV (TMDB trending endpoints).
    /// </summary>
    public async Task<(IReadOnlyList<DiscoveryTitleDto> Movies, IReadOnlyList<DiscoveryTitleDto> Tv)> GetTrendingAsync(
        string apiKey,
        string timeWindow,
        CancellationToken cancellationToken)
    {
        timeWindow = timeWindow is "day" or "week" ? timeWindow : "day";
        var moviesTask = ParseTrendingListAsync(
            $"{ApiBase}trending/movie/{timeWindow}?api_key={Uri.EscapeDataString(apiKey)}",
            "movie",
            cancellationToken);
        var tvTask = ParseTrendingListAsync(
            $"{ApiBase}trending/tv/{timeWindow}?api_key={Uri.EscapeDataString(apiKey)}",
            "tv",
            cancellationToken);
        await Task.WhenAll(moviesTask, tvTask).ConfigureAwait(false);
        return (await moviesTask.ConfigureAwait(false), await tvTask.ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<DiscoveryTitleDto>> ParseTrendingListAsync(
        string url,
        string mediaType,
        CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return [];
        }

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<DiscoveryTitleDto>();
        foreach (var el in results.EnumerateArray())
        {
            if (!el.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var id = idProp.GetInt32();
            var title = mediaType == "movie" ? GetString(el, "title") : GetString(el, "name");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            list.Add(new DiscoveryTitleDto
            {
                MediaType = mediaType,
                TmdbId = id,
                Title = title.Trim(),
                Overview = GetString(el, "overview"),
                PosterUrl = PosterUrl(GetString(el, "poster_path")),
                ReleaseYear = ParseYear(mediaType == "movie" ? GetString(el, "release_date") : GetString(el, "first_air_date"))
            });
        }

        return list;
    }

    /// <summary>
    /// Gets IMDb id for a TMDB movie.
    /// </summary>
    public async Task<string?> GetMovieImdbIdAsync(string apiKey, int tmdbMovieId, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}movie/{tmdbMovieId.ToString(CultureInfo.InvariantCulture)}/external_ids?api_key={Uri.EscapeDataString(apiKey)}";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return doc is null ? null : ReadImdbId(doc.RootElement);
    }

    /// <summary>
    /// Gets IMDb id for a TMDB TV series.
    /// </summary>
    public async Task<string?> GetTvImdbIdAsync(string apiKey, int tmdbTvId, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}tv/{tmdbTvId.ToString(CultureInfo.InvariantCulture)}/external_ids?api_key={Uri.EscapeDataString(apiKey)}";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return doc is null ? null : ReadImdbId(doc.RootElement);
    }

    /// <summary>
    /// Lists seasons with episode counts for a TV show.
    /// </summary>
    public async Task<IReadOnlyList<DiscoverySeasonDto>> GetTvSeasonsAsync(
        string apiKey,
        int tmdbTvId,
        CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}tv/{tmdbTvId.ToString(CultureInfo.InvariantCulture)}?api_key={Uri.EscapeDataString(apiKey)}";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return [];
        }

        var root = doc.RootElement;
        if (!root.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<DiscoverySeasonDto>();
        foreach (var el in seasons.EnumerateArray())
        {
            if (!el.TryGetProperty("season_number", out var sn) || sn.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var seasonNumber = sn.GetInt32();
            if (seasonNumber < 0)
            {
                continue;
            }

            var name = GetString(el, "name");
            var epCount = el.TryGetProperty("episode_count", out var ec) && ec.ValueKind == JsonValueKind.Number
                ? ec.GetInt32()
                : 0;

            list.Add(new DiscoverySeasonDto
            {
                SeasonNumber = seasonNumber,
                Name = string.IsNullOrWhiteSpace(name) ? FormattableString.Invariant($"Season {seasonNumber}") : name,
                EpisodeCount = epCount
            });
        }

        return list;
    }

    /// <summary>
    /// Lists episodes in a season.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveryEpisodeDto>> GetTvEpisodesAsync(
        string apiKey,
        int tmdbTvId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}tv/{tmdbTvId.ToString(CultureInfo.InvariantCulture)}/season/{seasonNumber.ToString(CultureInfo.InvariantCulture)}?api_key={Uri.EscapeDataString(apiKey)}";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return [];
        }

        if (!doc.RootElement.TryGetProperty("episodes", out var eps) || eps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<DiscoveryEpisodeDto>();
        foreach (var el in eps.EnumerateArray())
        {
            if (!el.TryGetProperty("episode_number", out var en) || en.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var epNum = en.GetInt32();
            var title = GetString(el, "name");
            list.Add(new DiscoveryEpisodeDto
            {
                EpisodeNumber = epNum,
                Title = string.IsNullOrWhiteSpace(title) ? FormattableString.Invariant($"Episode {epNum}") : title,
                Overview = GetString(el, "overview")
            });
        }

        return list;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.DebridStream/1.0");

            using var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("TMDB returned {Status} for {Url}", response.StatusCode, url);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "TMDB request failed");
            return null;
        }
    }

    private static string? ReadImdbId(JsonElement root)
    {
        if (!root.TryGetProperty("imdb_id", out var imdbProp) || imdbProp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = imdbProp.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        return raw.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? raw : "tt" + raw;
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return p.GetString();
    }

    private static int? ParseYear(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate) || isoDate.Length < 4)
        {
            return null;
        }

        return int.TryParse(isoDate.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            ? y
            : null;
    }
}
