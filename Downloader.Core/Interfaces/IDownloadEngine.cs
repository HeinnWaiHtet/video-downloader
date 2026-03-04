using Downloader.Core.Contracts;

namespace Downloader.Core.Interfaces;

public interface IDownloadEngine
{
    Task<MediaInfo> ProbeAsync(PageContext context, CancellationToken cancellationToken);

    Task<DownloadHandle> StartAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}
