using System.Net.Http;
using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Engines;

public sealed class DirectDownloadEngine : IDownloadEngine
{
    private static readonly HttpClient HttpClient = new();

    public Task<MediaInfo> ProbeAsync(PageContext context, CancellationToken cancellationToken)
    {
        var isDirectMedia = context.SourceUrl.AbsoluteUri.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            context.SourceUrl.AbsoluteUri.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                            context.SourceUrl.AbsoluteUri.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                            context.SourceUrl.AbsoluteUri.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);

        if (!isDirectMedia)
        {
            return Task.FromResult(MediaInfo.Blocked("not_direct_media", "Direct media URL not detected."));
        }

        var media = new MediaInfo(
            Title: context.PageTitle ?? Path.GetFileNameWithoutExtension(context.SourceUrl.AbsolutePath),
            ThumbnailUrl: null,
            Duration: null,
            Formats: new List<DownloadFormat> { new("direct", "Direct stream/file", "mp4", null, HasAudio: true, HasVideo: true) },
            HasAudio: true,
            HasVideo: true,
            Restrictions: Restrictions.None);

        return Task.FromResult(media);
    }

    public async Task<DownloadHandle> StartAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var outputFile = BuildOutputPath(request, "mp4");

        progress.Report(new DownloadProgress(id, DownloadState.Pending, 0, "Queued"));

        var completion = Task.Run(async () =>
        {
            progress.Report(new DownloadProgress(id, DownloadState.Downloading, 0, "Starting"));

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

            using var response = await HttpClient.GetAsync(request.SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(outputFile);

            var buffer = new byte[64 * 1024];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;

                var percent = total.HasValue && total.Value > 0
                    ? (double)written / total.Value * 100
                    : 0;

                progress.Report(new DownloadProgress(id, DownloadState.Downloading, percent, "Downloading", written, total));
            }

            progress.Report(new DownloadProgress(id, DownloadState.Completed, 100, "Completed", written, total));
        }, cancellationToken);

        return await Task.FromResult(
            new DownloadHandle(id, completion, () => { }, new System.Collections.ObjectModel.ReadOnlyCollection<string>(new[] { outputFile })));
    }

    private static string BuildOutputPath(DownloadRequest request, string extension)
    {
        var safeName = string.Join("_", request.FilenameTemplate.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        safeName = string.IsNullOrWhiteSpace(safeName) ? "video" : safeName;
        return Path.Combine(request.OutputPath, $"{safeName}.{extension}");
    }
}
