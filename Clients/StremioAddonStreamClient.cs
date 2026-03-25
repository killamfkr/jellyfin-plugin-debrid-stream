using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream.Clients;

/// <summary>
/// Fetches streams from a Stremio-compatible addon ({base}/stream/{movie|series}/{id}.json).
/// </summary>
public sealed class StremioAddonStreamClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StremioAddonStreamClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StremioAddonStreamClient"/> class.
    /// </summary>
    public StremioAddonStreamClient(IHttpClientFactory httpClientFactory, ILogger<StremioAddonStreamClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets candidate playback URLs or magnets from the addon.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetStreamUrlsAsync(
        string addonBaseUrl,
        string stremioType,
        string stremioVideoId,
        int maxCandidates,
        CancellationToken cancellationToken)
    {
        var baseUrl = addonBaseUrl.TrimEnd('/');
        // Stremio ids use colons (e.g. series tt123:1:2); keep path literal like official clients.
        var url = $"{baseUrl}/stream/{stremioType}/{stremioVideoId}.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.DebridStream/1.0");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stream addon returned {Status} for {Url}", response.StatusCode, url);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<string>();
        foreach (var el in streams.EnumerateArray())
        {
            if (results.Count >= maxCandidates)
            {
                break;
            }

            if (TryGetUrl(el, out var u) && !string.IsNullOrWhiteSpace(u))
            {
                results.Add(u.Trim());
            }
        }

        return results;
    }

    private static bool TryGetUrl(JsonElement stream, out string? url)
    {
        url = null;
        if (stream.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
        {
            url = urlProp.GetString();
            return !string.IsNullOrEmpty(url);
        }

        // Some addons use externalUrl
        if (stream.TryGetProperty("externalUrl", out var ext) && ext.ValueKind == JsonValueKind.String)
        {
            url = ext.GetString();
            return !string.IsNullOrEmpty(url);
        }

        return false;
    }
}
