namespace Jellyfin.Plugin.DebridStream.Clients;

/// <summary>
/// One stream line from a Stremio-compatible addon (<c>streams[]</c>).
/// </summary>
public sealed class StremioStreamEntry
{
    /// <summary>
    /// Gets the magnet or http(s) URL to resolve through debrid.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets a human-readable label for the client (from addon JSON).
    /// </summary>
    public required string DisplayName { get; init; }
}
