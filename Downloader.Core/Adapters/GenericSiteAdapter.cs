using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Adapters;

public sealed class GenericSiteAdapter : ISiteAdapter
{
    public string SiteName => "generic";

    public bool CanHandle(Uri pageUrl)
    {
        return pageUrl.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || pageUrl.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ProbeResult> ProbeAsync(PageContext context, CancellationToken cancellationToken)
    {
        if (!CanHandle(context.SourceUrl))
        {
            return Task.FromResult(new ProbeResult(SiteName, false, null, "unsupported_scheme"));
        }

        var title = context.PageTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"{context.SourceUrl.Host} video";
        }

        var media = new MediaInfo(
            Title: title,
            ThumbnailUrl: null,
            Duration: null,
            Formats: new List<DownloadFormat>
            {
                new("best", "Best available", "mp4", null, HasAudio: true, HasVideo: true),
                new("720p", "Up to 720p", "mp4", 720, HasAudio: true, HasVideo: true),
                new("audio", "Audio only", "m4a", null, HasAudio: true, HasVideo: false)
            },
            HasAudio: true,
            HasVideo: true,
            Restrictions: Restrictions.None);

        return Task.FromResult(new ProbeResult(SiteName, true, media));
    }
}
