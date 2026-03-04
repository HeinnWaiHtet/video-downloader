using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Adapters;

public sealed class FacebookAdapter : ISiteAdapter
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.facebook.com",
        "facebook.com",
        "m.facebook.com",
        "fb.watch"
    };

    public string SiteName => "facebook";

    public bool CanHandle(Uri pageUrl) => Hosts.Contains(pageUrl.Host);

    public Task<ProbeResult> ProbeAsync(PageContext context, CancellationToken cancellationToken)
    {
        if (!CanHandle(context.SourceUrl))
        {
            return Task.FromResult(new ProbeResult(SiteName, false, null, "unsupported_host"));
        }

        var media = new MediaInfo(
            Title: context.PageTitle ?? "Facebook video",
            ThumbnailUrl: null,
            Duration: null,
            Formats: new List<DownloadFormat>
            {
                new("best", "Best available", "mp4", null, HasAudio: true, HasVideo: true)
            },
            HasAudio: true,
            HasVideo: true,
            Restrictions: Restrictions.None);

        return Task.FromResult(new ProbeResult(SiteName, true, media));
    }
}
