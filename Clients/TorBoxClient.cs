using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DebridStream.Clients;

/// <summary>
/// Minimal TorBox API client (magnet create + poll + download link).
/// </summary>
public sealed class TorBoxClient
{
    private const string ApiBase = "https://api.torbox.app/v1/api/";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TorBoxClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TorBoxClient"/> class.
    /// </summary>
    public TorBoxClient(IHttpClientFactory httpClientFactory, ILogger<TorBoxClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a magnet to a direct download URL (TorBox cloud).
    /// </summary>
    public async Task<string?> ResolveMagnetAsync(string apiKey, string magnet, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.DebridStream/1.0");

        var url = ApiBase + "torrents/createtorrent?api_key=" + Uri.EscapeDataString(apiKey);
        using var content = new StringContent(
            JsonSerializer.Serialize(new { magnet = magnet }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("TorBox createtorrent failed: {Body}", body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        int? torrentId = null;
        if (root.TryGetProperty("torrent_id", out var tid) && tid.TryGetInt32(out var idInt))
        {
            torrentId = idInt;
        }
        else if (root.TryGetProperty("data", out var data) && data.TryGetProperty("torrent_id", out var tid2) && tid2.TryGetInt32(out var id2))
        {
            torrentId = id2;
        }

        if (torrentId is null)
        {
            return null;
        }

        const int maxAttempts = 40;
        for (var i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

            var reqUrl = ApiBase + "torrents/mylist?api_key=" + Uri.EscapeDataString(apiKey);
            using var listResp = await client.GetAsync(reqUrl, cancellationToken).ConfigureAwait(false);
            var listBody = await listResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!listResp.IsSuccessStatusCode)
            {
                continue;
            }

            using var listDoc = JsonDocument.Parse(listBody);
            if (!listDoc.RootElement.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) && id == torrentId.Value)
                {
                    var downloadReady = item.TryGetProperty("download", out var dl) && dl.ValueKind == JsonValueKind.True;
                    var progress = item.TryGetProperty("progress", out var pr) && pr.TryGetDouble(out var p) && p >= 100;

                    if (downloadReady || progress)
                    {
                        if (item.TryGetProperty("download_path", out var path) && path.ValueKind == JsonValueKind.String)
                        {
                            var pth = path.GetString();
                            if (!string.IsNullOrEmpty(pth) && pth.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                return pth;
                            }
                        }

                        // Request link endpoint (TorBox may expose get?torrent_id=)
                        var linkUrl = ApiBase + "torrents/requestdl?api_key=" + Uri.EscapeDataString(apiKey)
                            + "&torrent_id=" + torrentId.Value + "&zip=false";
                        using var linkResp = await client.GetAsync(linkUrl, cancellationToken).ConfigureAwait(false);
                        var linkBody = await linkResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (linkResp.IsSuccessStatusCode)
                        {
                            using var ld = JsonDocument.Parse(linkBody);
                            if (ld.RootElement.TryGetProperty("data", out var d))
                            {
                                if (d.ValueKind == JsonValueKind.String)
                                {
                                    return d.GetString();
                                }

                                if (d.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                                {
                                    return u.GetString();
                                }
                            }
                        }
                    }
                }
            }
        }

        return null;
    }
}
