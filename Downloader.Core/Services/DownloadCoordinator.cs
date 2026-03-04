using Downloader.Core.Compliance;
using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Services;

public sealed class DownloadCoordinator
{
    private readonly AdapterRegistry _adapterRegistry;
    private readonly IDownloadEngine _downloadEngine;
    private readonly ComplianceValidator _compliance;

    public DownloadCoordinator(AdapterRegistry adapterRegistry, IDownloadEngine downloadEngine, ComplianceValidator compliance)
    {
        _adapterRegistry = adapterRegistry;
        _downloadEngine = downloadEngine;
        _compliance = compliance;
    }

    public async Task<ProbeResult> DetectAsync(PageContext context, CancellationToken cancellationToken)
    {
        var adapter = _adapterRegistry.Resolve(context.SourceUrl);
        if (adapter is null)
        {
            return new ProbeResult("unknown", false, null, "unsupported_site");
        }

        var probe = await adapter.ProbeAsync(context, cancellationToken);
        if (!probe.IsSupported)
        {
            return probe;
        }

        var compliance = _compliance.ValidateProbe(probe.Site, probe.MediaInfo);
        if (!compliance.Allowed)
        {
            return new ProbeResult(probe.Site, false, null, compliance.Code);
        }

        return probe;
    }

    public async Task<DownloadHandle> StartDownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        // DetectAsync already performs media compliance checks.
        // At start time, enforce allowlist only and proceed directly to download.
        var siteCheck = _compliance.ValidateSite(request.Site);
        if (!siteCheck.Allowed)
        {
            throw new InvalidOperationException($"Blocked by policy: {siteCheck.Code} - {siteCheck.Message}");
        }

        return await _downloadEngine.StartAsync(request, progress, cancellationToken);
    }
}
