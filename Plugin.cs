using System.Collections.Generic;
using Jellyfin.Plugin.DebridStream.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DebridStream;

/// <summary>
/// Stremio-addon-style stream discovery with Real-Debrid / TorBox playback URLs for library movies and episodes.
/// </summary>
public class Plugin : BasePlugin<DebridStreamPluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the running plugin instance (set during DI construction).
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Debrid / Stremio streams";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("7b2c9e41-5f8a-4d3e-9c1b-0a2b3c4d5e6f");

    /// <inheritdoc />
    public override string Description =>
        "Library link picker plus dashboard Stream discovery (TMDB browse without library files). Stremio addon + Real-Debrid or TorBox; optional TMDB API for ids.";

    /// <inheritdoc />
    public override string ConfigurationFileName => "Jellyfin.Plugin.DebridStream.xml";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
        };

        yield return new PluginPageInfo
        {
            Name = "Stream discovery",
            DisplayName = "Stream discovery",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.discover.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "search"
        };
    }
}
