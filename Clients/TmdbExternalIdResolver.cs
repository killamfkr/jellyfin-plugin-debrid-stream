using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream.Clients;

/// <summary>
/// Uses the TMDB v3 API to map TMDB/TVDB ids on Jellyfin items to IMDb ids for Stremio-style addons.
/// Does not ship IMDb/TVDB data; requires a free TMDB API key when items lack IMDb.
/// </summary>
public sealed class TmdbExternalIdResolver
{
    private const string ApiBase = "https://api.themoviedb.org/3/";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbExternalIdResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbExternalIdResolver"/> class.
    /// </summary>
    public TmdbExternalIdResolver(IHttpClientFactory httpClientFactory, ILogger<TmdbExternalIdResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves an IMDb series id (tt…) for a TV show using provider ids on the series.
    /// </summary>
    public Task<string?> ResolveSeriesImdbIdAsync(
        IReadOnlyDictionary<string, string> seriesProviderIds,
        string? tmdbApiKey,
        CancellationToken cancellationToken)
    {
        if (TryNormalizeImdb(seriesProviderIds, out var direct))
        {
            return Task.FromResult<string?>(direct);
        }

        return ResolveSeriesImdbIdCoreAsync(seriesProviderIds, tmdbApiKey, cancellationToken);
    }

    /// <summary>
    /// Resolves an IMDb movie id (tt…) using provider ids on the movie.
    /// </summary>
    public Task<string?> ResolveMovieImdbIdAsync(
        IReadOnlyDictionary<string, string> movieProviderIds,
        string? tmdbApiKey,
        CancellationToken cancellationToken)
    {
        if (TryNormalizeImdb(movieProviderIds, out var direct))
        {
            return Task.FromResult<string?>(direct);
        }

        return ResolveMovieImdbIdCoreAsync(movieProviderIds, tmdbApiKey, cancellationToken);
    }

    private async Task<string?> ResolveSeriesImdbIdCoreAsync(
        IReadOnlyDictionary<string, string> seriesProviderIds,
        string? tmdbApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
        {
            return null;
        }

        var key = tmdbApiKey.Trim();

        if (TryGetProvider(seriesProviderIds, MetadataProvider.Tmdb, out var tmdbTvId))
        {
            var imdb = await FetchTvImdbAsync(tmdbTvId, key, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(imdb))
            {
                return imdb;
            }
        }

        if (TryGetProvider(seriesProviderIds, MetadataProvider.Tvdb, out var tvdbId))
        {
            var tmdbId = await FindTmdbTvIdByTvdbAsync(tvdbId, key, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(tmdbId))
            {
                return await FetchTvImdbAsync(tmdbId, key, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<string?> ResolveMovieImdbIdCoreAsync(
        IReadOnlyDictionary<string, string> movieProviderIds,
        string? tmdbApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tmdbApiKey))
        {
            return null;
        }

        var key = tmdbApiKey.Trim();

        if (TryGetProvider(movieProviderIds, MetadataProvider.Tmdb, out var tmdbMovieId))
        {
            var imdb = await FetchMovieImdbAsync(tmdbMovieId, key, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(imdb))
            {
                return imdb;
            }
        }

        if (TryGetProvider(movieProviderIds, MetadataProvider.Tvdb, out var tvdbId))
        {
            var tmdbId = await FindTmdbMovieIdByTvdbAsync(tvdbId, key, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(tmdbId))
            {
                return await FetchMovieImdbAsync(tmdbId, key, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<string?> FetchMovieImdbAsync(string tmdbMovieId, string apiKey, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}movie/{Uri.EscapeDataString(tmdbMovieId)}/external_ids?api_key={Uri.EscapeDataString(apiKey)}";
        return await ReadImdbFromJsonAsync(url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> FetchTvImdbAsync(string tmdbTvId, string apiKey, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}tv/{Uri.EscapeDataString(tmdbTvId)}/external_ids?api_key={Uri.EscapeDataString(apiKey)}";
        return await ReadImdbFromJsonAsync(url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> FindTmdbTvIdByTvdbAsync(string tvdbSeriesId, string apiKey, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}find/{Uri.EscapeDataString(tvdbSeriesId)}?api_key={Uri.EscapeDataString(apiKey)}&external_source=tvdb_id";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("tv_results", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var el in arr.EnumerateArray())
        {
            if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                return idProp.GetInt32().ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private async Task<string?> FindTmdbMovieIdByTvdbAsync(string tvdbMovieId, string apiKey, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}find/{Uri.EscapeDataString(tvdbMovieId)}?api_key={Uri.EscapeDataString(apiKey)}&external_source=tvdb_id";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("movie_results", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var el in arr.EnumerateArray())
        {
            if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                return idProp.GetInt32().ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private async Task<string?> ReadImdbFromJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("imdb_id", out var imdbProp) || imdbProp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = imdbProp.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : NormalizeImdbString(raw);
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
                _logger.LogDebug("TMDB request failed {Status} for {Url}", response.StatusCode, url);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "TMDB request error for {Url}", url);
            return null;
        }
    }

    private static bool TryGetProvider(
        IReadOnlyDictionary<string, string> providerIds,
        MetadataProvider provider,
        out string value)
    {
        value = string.Empty;
        var key = provider.ToString();
        if (!providerIds.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static bool TryNormalizeImdb(IReadOnlyDictionary<string, string> providerIds, out string imdb)
    {
        imdb = string.Empty;
        if (!TryGetProvider(providerIds, MetadataProvider.Imdb, out var raw))
        {
            return false;
        }

        var n = NormalizeImdbString(raw);
        if (n is null)
        {
            return false;
        }

        imdb = n;
        return true;
    }

    private static string? NormalizeImdbString(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        if (raw.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return "tt" + raw;
    }
}
