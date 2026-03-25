using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream.Clients;

/// <summary>
/// Minimal Real-Debrid REST client: unrestrict links and resolve magnets via torrents API.
/// </summary>
public sealed class RealDebridClient
{
    private const string ApiBase = "https://api.real-debrid.com/rest/1.0/";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RealDebridClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RealDebridClient"/> class.
    /// </summary>
    public RealDebridClient(IHttpClientFactory httpClientFactory, ILogger<RealDebridClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a magnet or http/https link to a direct playback URL.
    /// </summary>
    public async Task<string?> ResolvePlaybackUrlAsync(string token, string magnetOrUrl, CancellationToken cancellationToken)
    {
        var client = CreateClient(token);
        if (magnetOrUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveMagnetAsync(client, magnetOrUrl, cancellationToken).ConfigureAwait(false);
        }

        if (magnetOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || magnetOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await UnrestrictLinkAsync(client, magnetOrUrl, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private HttpClient CreateClient(string token)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.DebridStream/1.0");
        return client;
    }

    private async Task<string?> UnrestrictLinkAsync(HttpClient client, string link, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["link"] = link });
        using var response = await client.PostAsync(ApiBase + "unrestrict/link", content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("RD unrestrict failed: {Body}", body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("download", out var d) && d.ValueKind == JsonValueKind.String)
        {
            return d.GetString();
        }

        return null;
    }

    private async Task<string?> ResolveMagnetAsync(HttpClient client, string magnet, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["magnet"] = magnet });
        using var response = await client.PostAsync(ApiBase + "torrents/addMagnet", content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("RD addMagnet failed: {Body}", body);
            return null;
        }

        using var addDoc = JsonDocument.Parse(body);
        if (!addDoc.RootElement.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var torrentId = idEl.GetString()!;
        const int maxAttempts = 45;
        for (var i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

            using var infoReq = new HttpRequestMessage(HttpMethod.Get, ApiBase + "torrents/info/" + Uri.EscapeDataString(torrentId));
            using var infoResp = await client.SendAsync(infoReq, cancellationToken).ConfigureAwait(false);
            var infoBody = await infoResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!infoResp.IsSuccessStatusCode)
            {
                continue;
            }

            using var info = JsonDocument.Parse(infoBody);
            var status = info.RootElement.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString()
                : null;

            if (string.Equals(status, "waiting_files_selection", StringComparison.OrdinalIgnoreCase))
            {
                using var selContent = new StringContent("files=all", Encoding.UTF8, "application/x-www-form-urlencoded");
                using var selResp = await client.PostAsync(ApiBase + "torrents/selectFiles/" + Uri.EscapeDataString(torrentId), selContent, cancellationToken).ConfigureAwait(false);
                _ = await selResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.Equals(status, "downloaded", StringComparison.OrdinalIgnoreCase))
            {
                if (info.RootElement.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
                {
                    foreach (var linkEl in links.EnumerateArray())
                    {
                        if (linkEl.ValueKind == JsonValueKind.String)
                        {
                            var hosterLink = linkEl.GetString();
                            if (!string.IsNullOrEmpty(hosterLink))
                            {
                                var direct = await UnrestrictLinkAsync(client, hosterLink, cancellationToken).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(direct))
                                {
                                    return direct;
                                }
                            }
                        }
                    }
                }

                return null;
            }

            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return null;
    }
}
