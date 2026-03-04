using System.Text.Json.Serialization;
using Downloader.Core.Contracts;

namespace Downloader.Core.Ipc;

public sealed record NativeEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] object Payload,
    [property: JsonPropertyName("requestId")] string? RequestId = null);

public sealed record DetectRequest(string Url, string? PageTitle);

public sealed record DetectResponse(bool Supported, string Site, MediaInfo? MediaInfo, string? Reason = null);

public sealed record StartDownloadRequest(
    string Url,
    string Site,
    string SelectedFormatId,
    string? OutputPath,
    string FilenameTemplate,
    AuthContext? AuthContext = null,
    bool WaitForCompletion = false);

public sealed record StartDownloadResponse(
    bool Accepted,
    string? DownloadId = null,
    string? Error = null,
    bool Completed = false,
    IReadOnlyList<string>? Files = null);

public sealed record CancelRequest(string DownloadId);

public sealed record CancelResponse(bool Cancelled, string DownloadId, string? Error = null);

public sealed record ProgressEvent(string DownloadId, DownloadState State, double Percent, string Status);

public sealed record CompletedEvent(string DownloadId, IReadOnlyList<string>? Files);

public sealed record ErrorEvent(string DownloadId, string ErrorCode, string Message);

public sealed record HealthCheckRequest(string ClientVersion);

public sealed record HealthCheckResponse(string Status, string HostVersion, string Runtime);

public sealed record PickFolderRequest(string? InitialPath = null);

public sealed record PickFolderResponse(bool Success, string? Path = null, string? Error = null);
