namespace Jellyfin.Plugin.DebridStream.Api;

/// <summary>
/// JSON DTOs for the discovery API and TMDB client.
/// </summary>
public sealed class DiscoveryStatusDto
{
    /// <summary>
    /// Gets or sets a value indicating whether discovery can run.
    /// </summary>
    public bool Ready { get; set; }

    /// <summary>
    /// Gets or sets an optional user-facing message when not ready.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the TMDB API key is set.
    /// </summary>
    public bool HasTmdbKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether debrid streams are enabled in settings.
    /// </summary>
    public bool StreamsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a stream addon URL is configured.
    /// </summary>
    public bool HasStreamAddon { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Real-Debrid or TorBox credentials are set.
    /// </summary>
    public bool HasDebrid { get; set; }
}

/// <summary>
/// A row from search or trending.
/// </summary>
public sealed class DiscoveryTitleDto
{
    /// <summary>
    /// Gets or sets <c>movie</c> or <c>tv</c>.
    /// </summary>
    public required string MediaType { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id.
    /// </summary>
    public int TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the overview text.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the poster image URL.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the release or first-air year.
    /// </summary>
    public int? ReleaseYear { get; set; }
}

/// <summary>
/// One TV season.
/// </summary>
public sealed class DiscoverySeasonDto
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the season title.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the episode count.
    /// </summary>
    public int EpisodeCount { get; set; }
}

/// <summary>
/// One TV episode.
/// </summary>
public sealed class DiscoveryEpisodeDto
{
    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the overview.
    /// </summary>
    public string? Overview { get; set; }
}

/// <summary>
/// One stream line from the Stremio addon.
/// </summary>
public sealed class DiscoveryStreamLineDto
{
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the magnet or http(s) URL to resolve through debrid.
    /// </summary>
    public required string Url { get; set; }
}

/// <summary>
/// Request body for resolve.
/// </summary>
public sealed class DiscoveryResolveRequestDto
{
    /// <summary>
    /// Gets or sets the magnet or http(s) URL from the addon.
    /// </summary>
    public string? Url { get; set; }
}

/// <summary>
/// Resolved playback response.
/// </summary>
public sealed class DiscoveryResolveResponseDto
{
    /// <summary>
    /// Gets or sets the direct HTTP playback URL.
    /// </summary>
    public string? PlaybackUrl { get; set; }

    /// <summary>
    /// Gets or sets a hint for the container (e.g. mkv).
    /// </summary>
    public string? Container { get; set; }
}

/// <summary>
/// Trending bucket response.
/// </summary>
public sealed class DiscoveryTrendingDto
{
    /// <summary>
    /// Gets or sets trending movies.
    /// </summary>
    public IReadOnlyList<DiscoveryTitleDto> Movies { get; set; } = [];

    /// <summary>
    /// Gets or sets trending TV.
    /// </summary>
    public IReadOnlyList<DiscoveryTitleDto> Tv { get; set; } = [];
}
