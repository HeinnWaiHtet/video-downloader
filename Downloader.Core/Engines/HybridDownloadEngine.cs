using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Engines;

public sealed class HybridDownloadEngine : IDownloadEngine
{
    private readonly IDownloadEngine _direct;
    private readonly IDownloadEngine _ytDlp;

    public HybridDownloadEngine(IDownloadEngine direct, IDownloadEngine ytDlp)
    {
        _direct = direct;
        _ytDlp = ytDlp;
    }

    public async Task<MediaInfo> ProbeAsync(PageContext context, CancellationToken cancellationToken)
    {
        var direct = await _direct.ProbeAsync(context, cancellationToken);
        if (!direct.Restrictions.RequiresUnsupportedBypass)
        {
            return direct;
        }

        return await _ytDlp.ProbeAsync(context, cancellationToken);
    }

    public async Task<DownloadHandle> StartAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var directProbe = await _direct.ProbeAsync(new PageContext(request.SourceUrl, null), cancellationToken);
        if (!directProbe.Restrictions.RequiresUnsupportedBypass)
        {
            return await _direct.StartAsync(request, progress, cancellationToken);
        }

        return await _ytDlp.StartAsync(request, progress, cancellationToken);
    }
}
