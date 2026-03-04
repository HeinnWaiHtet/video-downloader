using Downloader.Core.Contracts;

namespace Downloader.Core.Compliance;

public sealed class ComplianceValidator
{
    private readonly HashSet<string> _allowedSites;

    public ComplianceValidator(IEnumerable<string>? allowedSites = null)
    {
        _allowedSites = new HashSet<string>(allowedSites ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public bool EnforceAllowList => _allowedSites.Count > 0;

    public ComplianceResult ValidateSite(string site)
    {
        if (EnforceAllowList && !_allowedSites.Contains(site))
        {
            return ComplianceResult.Blocked("site_not_allowed", "This site is not enabled by your policy.");
        }

        return ComplianceResult.Permitted();
    }

    public ComplianceResult ValidateProbe(string site, MediaInfo? media)
    {
        var siteCheck = ValidateSite(site);
        if (!siteCheck.Allowed)
        {
            return siteCheck;
        }

        if (media is null)
        {
            return ComplianceResult.Blocked("media_not_found", "No downloadable media was detected.");
        }

        if (media.Restrictions.IsDrmProtected)
        {
            return ComplianceResult.Blocked("drm_protected", "This media is DRM protected and cannot be downloaded.");
        }

        if (media.Restrictions.RequiresUnsupportedBypass)
        {
            return ComplianceResult.Blocked(
                media.Restrictions.LegalWarningCode ?? "unsupported_protection",
                media.Restrictions.Message ?? "This media requires unsupported protection bypass and is blocked.");
        }

        return ComplianceResult.Permitted();
    }
}

public sealed record ComplianceResult(bool Allowed, string? Code, string? Message)
{
    public static ComplianceResult Permitted() => new(true, null, null);

    public static ComplianceResult Blocked(string code, string message) => new(false, code, message);
}
