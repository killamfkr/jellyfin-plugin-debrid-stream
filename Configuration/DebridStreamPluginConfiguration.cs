using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DebridStream.Configuration;

/// <summary>
/// Settings for Stremio-style stream discovery and debrid resolution.
/// </summary>
public class DebridStreamPluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether extra debrid sources are offered for movies and episodes.
    /// </summary>
    public bool EnableDebridStreams { get; set; }

    /// <summary>
    /// Gets or sets which backend resolves hosters and magnets (0 = Real-Debrid, 1 = TorBox).
    /// </summary>
    public int DebridBackend { get; set; }

    /// <summary>
    /// Gets or sets the Real-Debrid API token (from https://real-debrid.com/apitoken).
    /// </summary>
    public string RealDebridApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TorBox API key.
    /// </summary>
    public string TorBoxApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Stremio-compatible addon base URL (no trailing slash), e.g. https://torrentio.strem.fun.
    /// The plugin requests {Base}/stream/{movie|series}/{id}.json.
    /// </summary>
    public string StreamAddonBaseUrl { get; set; } = "https://torrentio.strem.fun";

    /// <summary>
    /// Gets or sets how many stream candidates to try in order from the addon response.
    /// </summary>
    public int MaxStreamCandidates { get; set; } = 8;
}
