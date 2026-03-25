#nullable disable

#pragma warning disable CA1711, CS1591

using System.Globalization;
using System.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.DebridStream;

/// <summary>
/// Minimal <see cref="ILiveStream"/> for resolved HTTP playback URLs (same idea as Jellyfin.LiveTv.IO.ExclusiveLiveStream).
/// </summary>
internal sealed class DebridExclusiveLiveStream : ILiveStream
{
    private readonly Func<Task> _closeFn;

    public DebridExclusiveLiveStream(MediaSourceInfo mediaSource, Func<Task> closeFn)
    {
        MediaSource = mediaSource;
        EnableStreamSharing = false;
        _closeFn = closeFn;
        ConsumerCount = 1;
        UniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    public int ConsumerCount { get; set; }

    public string OriginalStreamId { get; set; }

    public string TunerHostId => null;

    public bool EnableStreamSharing { get; set; }

    public MediaSourceInfo MediaSource { get; set; }

    public string UniqueId { get; }

    public Task Close() => _closeFn();

    public Stream GetStream() => throw new NotSupportedException();

    public Task Open(CancellationToken openCancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
