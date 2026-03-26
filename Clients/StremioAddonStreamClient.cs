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
    /// Gets stream entries (URL + display label) from the addon, up to <paramref name="maxEntries"/>.
    /// </summary>
    public async Task<IReadOnlyList<StremioStreamEntry>> GetStreamEntriesAsync(
        string addonBaseUrl,
        string stremioType,
        string stremioVideoId,
        int maxEntries,
        CancellationToken cancellationToken)
    {
        var baseUrl = addonBaseUrl.TrimEnd('/');
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

        var results = new List<StremioStreamEntry>();
        var index = 0;
        foreach (var el in streams.EnumerateArray())
        {
            if (results.Count >= maxEntries)
            {
                break;
            }

            if (!TryGetUrl(el, out var u) || string.IsNullOrWhiteSpace(u))
            {
                continue;
            }

            index++;
            var label = BuildDisplayName(el, index);
            results.Add(new StremioStreamEntry
            {
                Url = u.Trim(),
                DisplayName = label
            });
        }

        return results;
    }

    private static string BuildDisplayName(JsonElement stream, int ordinal)
    {
        var parts = new List<string>();

        if (TryGetString(stream, "title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title.Trim());
        }

        if (TryGetString(stream, "name", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            var t = name.Trim();
            if (parts.Count == 0 || !string.Equals(parts[0], t, StringComparison.Ordinal))
            {
                parts.Add(t);
            }
        }

        if (parts.Count == 0 && TryGetString(stream, "description", out var desc) && !string.IsNullOrWhiteSpace(desc))
        {
            var d = desc.Trim();
            if (d.Length > 120)
            {
                d = d[..120] + "…";
            }

            parts.Add(d);
        }

        if (parts.Count == 0)
        {
            return FormattableString.Invariant($"Stream {ordinal}");
        }

        return string.Join(" — ", parts);
    }

    private static bool TryGetString(JsonElement el, string property, out string value)
    {
        value = string.Empty;
        if (!el.TryGetProperty(property, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = p.GetString() ?? string.Empty;
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetUrl(JsonElement stream, out string? url)
    {
        url = null;
        if (stream.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
        {
            url = urlProp.GetString();
            return !string.IsNullOrEmpty(url);
        }

        if (stream.TryGetProperty("externalUrl", out var ext) && ext.ValueKind == JsonValueKind.String)
        {
            url = ext.GetString();
            return !string.IsNullOrEmpty(url);
        }

        return false;
    }
}
