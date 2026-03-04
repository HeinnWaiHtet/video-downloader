using Downloader.Core.Contracts;

namespace Downloader.Core.Interfaces;

public interface ISiteAdapter
{
    string SiteName { get; }

    bool CanHandle(Uri pageUrl);

    Task<ProbeResult> ProbeAsync(PageContext context, CancellationToken cancellationToken);
}
