using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Adapters;

public sealed class YouTubeAdapter : ISiteAdapter
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.youtube.com",
        "youtube.com",
        "m.youtube.com",
        "youtu.be"
    };

    public string SiteName => "youtube";

    public bool CanHandle(Uri pageUrl) => Hosts.Contains(pageUrl.Host);

    public Task<ProbeResult> ProbeAsync(PageContext context, CancellationToken cancellationToken)
    {
        if (!CanHandle(context.SourceUrl))
        {
            return Task.FromResult(new ProbeResult(SiteName, false, null, "unsupported_host"));
        }

        var formats = new List<DownloadFormat>
        {
            new("best", "Best available", "mp4", null, HasAudio: true, HasVideo: true),
            new("1080p", "1080p (video+audio or merged)", "mp4", 1080, HasAudio: true, HasVideo: true),
            new("720p", "720p", "mp4", 720, HasAudio: true, HasVideo: true),
            new("audio", "Audio only", "m4a", null, HasAudio: true, HasVideo: false)
        };

        var media = new MediaInfo(
            Title: context.PageTitle ?? "YouTube video",
            ThumbnailUrl: null,
            Duration: null,
            Formats: formats,
            HasAudio: true,
            HasVideo: true,
            Restrictions: Restrictions.None);

        return Task.FromResult(new ProbeResult(SiteName, true, media));
    }
}
