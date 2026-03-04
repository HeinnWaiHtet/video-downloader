using System.Collections.ObjectModel;

namespace Downloader.Core.Contracts;

public sealed record PageContext(Uri SourceUrl, string? PageTitle, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AuthContext(string? CookieFilePath = null, string? AccessToken = null);

public sealed record DownloadFormat(string Id, string Label, string Container, int? Height, bool HasAudio, bool HasVideo, long? EstimatedSizeBytes = null);

public sealed record Restrictions(bool IsDrmProtected, bool RequiresUnsupportedBypass, string? LegalWarningCode, string? Message = null)
{
    public static Restrictions None { get; } = new(false, false, null, null);
}

public sealed record MediaInfo(
    string Title,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    IReadOnlyList<DownloadFormat> Formats,
    bool HasAudio,
    bool HasVideo,
    Restrictions Restrictions)
{
    public static MediaInfo Blocked(string reasonCode, string message) =>
        new(
            Title: "Unavailable",
            ThumbnailUrl: null,
            Duration: null,
            Formats: Array.Empty<DownloadFormat>(),
            HasAudio: false,
            HasVideo: false,
            Restrictions: new Restrictions(false, true, reasonCode, message));
}

public sealed record DownloadRequest(
    Uri SourceUrl,
    string Site,
    string SelectedFormatId,
    string OutputPath,
    string FilenameTemplate,
    AuthContext? AuthContext = null);

public enum DownloadState
{
    Pending,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public sealed record DownloadProgress(
    string DownloadId,
    DownloadState State,
    double Percent,
    string Status,
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    double? SpeedBytesPerSecond = null);

public sealed record DownloadHandle(
    string DownloadId,
    Task Completion,
    Action Cancel,
    ReadOnlyCollection<string>? Artifacts = null);

public sealed record ProbeResult(string Site, bool IsSupported, MediaInfo? MediaInfo, string? UnsupportedReason = null);
