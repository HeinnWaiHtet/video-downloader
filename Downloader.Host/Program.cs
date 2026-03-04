using System.Text.Json;
using Downloader.Core.Adapters;
using Downloader.Core.Compliance;
using Downloader.Core.Contracts;
using Downloader.Core.Engines;
using Downloader.Core.Ipc;
using Downloader.Core.Interfaces;
using Downloader.Core.Native;
using Downloader.Core.Services;

var runtimeDir = Path.Combine(AppContext.BaseDirectory, "runtime");
Directory.CreateDirectory(runtimeDir);
var inboxPath = Path.Combine(runtimeDir, "ui-inbox.jsonl");
var debugDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".authorized-downloader");
Directory.CreateDirectory(debugDir);
var debugLogPath = Path.Combine(debugDir, "host-debug.log");

var adapters = new AdapterRegistry(new ISiteAdapter[]
{
    new YouTubeAdapter(),
    new FacebookAdapter()
});

var engine = new HybridDownloadEngine(new DirectDownloadEngine(), new YtDlpDownloadEngine());
var compliance = new ComplianceValidator(new[] { "youtube", "facebook" });
var coordinator = new DownloadCoordinator(adapters, engine, compliance);

var activeDownloads = new Dictionary<string, CancellationTokenSource>();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
};

using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();

while (true)
{
    var raw = await NativeMessageFraming.ReadMessageAsync(input, CancellationToken.None);
    if (raw is null)
    {
        break;
    }

    NativeEnvelope? envelope;
    try
    {
        envelope = JsonSerializer.Deserialize<NativeEnvelope>(raw, json);
    }
    catch (Exception ex)
    {
        await SendErrorAsync(output, "unknown", "invalid_message", ex.Message, json);
        continue;
    }

    if (envelope is null)
    {
        await SendErrorAsync(output, "unknown", "invalid_message", "Envelope was null.", json);
        continue;
    }

    var payloadJson = JsonSerializer.Serialize(envelope.Payload, json);
    await AppendDebugLogAsync(debugLogPath, $"RECV type={envelope.Type} requestId={envelope.RequestId} payload={payloadJson}");

    try
    {
        switch (envelope.Type)
        {
            case "healthCheck":
                await HandleHealthCheckAsync(output, envelope.RequestId, json);
                break;
            case "detect":
                await HandleDetectAsync(output, coordinator, payloadJson, envelope.RequestId, json, inboxPath);
                break;
            case "startDownload":
                await HandleStartDownloadAsync(output, coordinator, payloadJson, envelope.RequestId, json, inboxPath, activeDownloads, debugLogPath);
                break;
            case "pickFolder":
                await HandlePickFolderAsync(output, payloadJson, envelope.RequestId, json);
                break;
            case "cancel":
                await HandleCancelAsync(output, payloadJson, envelope.RequestId, json, activeDownloads);
                break;
            default:
                await SendErrorAsync(output, envelope.RequestId, "unknown_type", $"Unknown message type: {envelope.Type}", json);
                await AppendDebugLogAsync(debugLogPath, $"ERR unknown_type type={envelope.Type} requestId={envelope.RequestId}");
                break;
        }
    }
    catch (Exception ex)
    {
        await AppendDebugLogAsync(debugLogPath, $"EXCEPTION type={envelope.Type} requestId={envelope.RequestId} message={ex}");
        await SendErrorAsync(output, envelope.RequestId, "host_error", ex.Message, json);
    }
}

static async Task HandleHealthCheckAsync(Stream output, string? requestId, JsonSerializerOptions json)
{
    var response = new HealthCheckResponse("ok", "1.0.0", Environment.Version.ToString());
    await SendEnvelopeAsync(output, new NativeEnvelope("healthCheckResponse", response, requestId), json);
}

static async Task HandleDetectAsync(
    Stream output,
    DownloadCoordinator coordinator,
    string payloadJson,
    string? requestId,
    JsonSerializerOptions json,
    string inboxPath)
{
    var request = JsonSerializer.Deserialize<DetectRequest>(payloadJson, json)
                  ?? throw new InvalidOperationException("Invalid detect payload.");

    var sourceUri = new Uri(request.Url);
    var result = await coordinator.DetectAsync(new PageContext(sourceUri, request.PageTitle), CancellationToken.None);
    var response = new DetectResponse(result.IsSupported, result.Site, result.MediaInfo, result.UnsupportedReason);

    await SendEnvelopeAsync(output, new NativeEnvelope("detectResponse", response, requestId), json);
    await AppendInboxAsync(inboxPath, "detect", response);
}

static async Task HandleStartDownloadAsync(
    Stream output,
    DownloadCoordinator coordinator,
    string payloadJson,
    string? requestId,
    JsonSerializerOptions json,
    string inboxPath,
    Dictionary<string, CancellationTokenSource> activeDownloads,
    string debugLogPath)
{
    var request = JsonSerializer.Deserialize<StartDownloadRequest>(payloadJson, json)
                  ?? throw new InvalidOperationException("Invalid start download payload.");
    await AppendDebugLogAsync(debugLogPath, $"START requestId={requestId} url={request.Url} site={request.Site} format={request.SelectedFormatId} output={request.OutputPath} wait={request.WaitForCompletion}");

    var cts = new CancellationTokenSource();
    var selectedFormat = string.IsNullOrWhiteSpace(request.SelectedFormatId) ? "best" : request.SelectedFormatId;
    var outputPath = ResolveOutputPath(request.OutputPath);

    var downloadRequest = new DownloadRequest(
        SourceUrl: new Uri(request.Url),
        Site: request.Site,
        SelectedFormatId: selectedFormat,
        OutputPath: outputPath,
        FilenameTemplate: string.IsNullOrWhiteSpace(request.FilenameTemplate) ? "video" : request.FilenameTemplate,
        AuthContext: request.AuthContext);

    var progress = new Progress<DownloadProgress>(async p =>
    {
        await AppendInboxAsync(inboxPath, "progress", p);
        if (!request.WaitForCompletion)
        {
            await SendEnvelopeAsync(output, new NativeEnvelope("progress", new ProgressEvent(p.DownloadId, p.State, p.Percent, p.Status)), json);
        }
    });

    var handle = await coordinator.StartDownloadAsync(downloadRequest, progress, cts.Token);
    activeDownloads[handle.DownloadId] = cts;
    await AppendDebugLogAsync(debugLogPath, $"START accepted requestId={requestId} downloadId={handle.DownloadId}");

    if (request.WaitForCompletion)
    {
        try
        {
            await handle.Completion;
            await AppendInboxAsync(inboxPath, "completed", new { handle.DownloadId, handle.Artifacts });
            await AppendDebugLogAsync(debugLogPath, $"START completed requestId={requestId} downloadId={handle.DownloadId}");
            await SendEnvelopeAsync(
                output,
                new NativeEnvelope(
                    "startDownloadResponse",
                    new StartDownloadResponse(true, handle.DownloadId, Completed: true, Files: handle.Artifacts?.ToArray()),
                    requestId),
                json);
        }
        catch (OperationCanceledException)
        {
            await AppendInboxAsync(inboxPath, "cancelled", new { handle.DownloadId });
            await AppendDebugLogAsync(debugLogPath, $"START cancelled requestId={requestId} downloadId={handle.DownloadId}");
            await SendEnvelopeAsync(
                output,
                new NativeEnvelope(
                    "startDownloadResponse",
                    new StartDownloadResponse(false, handle.DownloadId, "Download cancelled."),
                    requestId),
                json);
        }
        catch (Exception ex)
        {
            await AppendInboxAsync(inboxPath, "failed", new { handle.DownloadId, ex.Message });
            await AppendDebugLogAsync(debugLogPath, $"START failed requestId={requestId} downloadId={handle.DownloadId} error={ex}");
            await SendEnvelopeAsync(
                output,
                new NativeEnvelope(
                    "startDownloadResponse",
                    new StartDownloadResponse(false, handle.DownloadId, ex.Message),
                    requestId),
                json);
        }
        finally
        {
            activeDownloads.Remove(handle.DownloadId);
        }
        return;
    }

    _ = Task.Run(async () =>
    {
        try
        {
            await handle.Completion;
            await SendEnvelopeAsync(output, new NativeEnvelope("completed", new CompletedEvent(handle.DownloadId, handle.Artifacts?.ToArray())), json);
            await AppendInboxAsync(inboxPath, "completed", new { handle.DownloadId, handle.Artifacts });
        }
        catch (OperationCanceledException)
        {
            await SendEnvelopeAsync(output, new NativeEnvelope("error", new ErrorEvent(handle.DownloadId, "cancelled", "Download cancelled.")), json);
            await AppendInboxAsync(inboxPath, "cancelled", new { handle.DownloadId });
        }
        catch (Exception ex)
        {
            await SendEnvelopeAsync(output, new NativeEnvelope("error", new ErrorEvent(handle.DownloadId, "download_failed", ex.Message)), json);
            await AppendInboxAsync(inboxPath, "failed", new { handle.DownloadId, ex.Message });
        }
        finally
        {
            activeDownloads.Remove(handle.DownloadId);
        }
    });

    await SendEnvelopeAsync(output, new NativeEnvelope("startDownloadResponse", new StartDownloadResponse(true, handle.DownloadId), requestId), json);
    await AppendInboxAsync(inboxPath, "accepted", new { handle.DownloadId, request.Url, request.Site });
}

static async Task HandlePickFolderAsync(
    Stream output,
    string payloadJson,
    string? requestId,
    JsonSerializerOptions json)
{
    var request = JsonSerializer.Deserialize<PickFolderRequest>(payloadJson, json) ?? new PickFolderRequest();
    var response = await TryPickFolderAsync(request);
    await SendEnvelopeAsync(output, new NativeEnvelope("pickFolderResponse", response, requestId), json);
}

static async Task HandleCancelAsync(
    Stream output,
    string payloadJson,
    string? requestId,
    JsonSerializerOptions json,
    Dictionary<string, CancellationTokenSource> activeDownloads)
{
    var request = JsonSerializer.Deserialize<CancelRequest>(payloadJson, json)
                  ?? throw new InvalidOperationException("Invalid cancel payload.");

    if (!activeDownloads.TryGetValue(request.DownloadId, out var cts))
    {
        await SendEnvelopeAsync(output, new NativeEnvelope("cancelResponse", new CancelResponse(false, request.DownloadId, "Unknown download id."), requestId), json);
        return;
    }

    cts.Cancel();
    await SendEnvelopeAsync(output, new NativeEnvelope("cancelResponse", new CancelResponse(true, request.DownloadId), requestId), json);
}

static async Task SendEnvelopeAsync(Stream output, NativeEnvelope envelope, JsonSerializerOptions json)
{
    var raw = JsonSerializer.Serialize(envelope, json);
    await NativeMessageFraming.WriteMessageAsync(output, raw, CancellationToken.None);
}

static Task SendErrorAsync(Stream output, string? requestId, string code, string message, JsonSerializerOptions json)
{
    return SendEnvelopeAsync(output, new NativeEnvelope("error", new ErrorEvent("unknown", code, message), requestId), json);
}

static string ResolveOutputPath(string? outputPath)
{
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        return outputPath;
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, "Downloads");
}

static async Task<PickFolderResponse> TryPickFolderAsync(PickFolderRequest request)
{
    try
    {
        if (OperatingSystem.IsMacOS())
        {
            var escaped = EscapeAppleScriptString(request.InitialPath);
            var script = string.IsNullOrWhiteSpace(escaped)
                ? "POSIX path of (choose folder with prompt \"Select download folder\")"
                : $"POSIX path of (choose folder with prompt \"Select download folder\" default location POSIX file \"{escaped}\")";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return new PickFolderResponse(false, Error: "Failed to start osascript.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return new PickFolderResponse(false, Error: string.IsNullOrWhiteSpace(stderr) ? "Folder selection cancelled." : stderr.Trim());
            }

            var folder = stdout.Trim();
            return string.IsNullOrWhiteSpace(folder)
                ? new PickFolderResponse(false, Error: "No folder selected.")
                : new PickFolderResponse(true, folder);
        }

        return new PickFolderResponse(false, Error: "Folder picker is currently implemented for macOS only in this MVP.");
    }
    catch (Exception ex)
    {
        return new PickFolderResponse(false, Error: ex.Message);
    }
}

static string EscapeAppleScriptString(string? value)
{
    return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}

static async Task AppendInboxAsync<T>(string path, string type, T payload)
{
    var line = JsonSerializer.Serialize(new
    {
        Timestamp = DateTimeOffset.UtcNow,
        Type = type,
        Payload = payload
    });

    await File.AppendAllTextAsync(path, line + Environment.NewLine);
}

static Task AppendDebugLogAsync(string path, string message)
{
    var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}";
    return File.AppendAllTextAsync(path, line);
}
