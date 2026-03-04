using System.Collections.Concurrent;
using System.Diagnostics;
using Downloader.Core.Adapters;
using Downloader.Core.Compliance;
using Downloader.Core.Contracts;
using Downloader.Core.Engines;
using Downloader.Core.Interfaces;
using Downloader.Core.Services;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5077");
}
else
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
var debugLogsEnabled = string.Equals(Environment.GetEnvironmentVariable("UI_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(debugLogsEnabled ? LogLevel.Information : LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", debugLogsEnabled ? LogLevel.Information : LogLevel.Warning);

builder.Services.AddSingleton(_ =>
{
    var adapterRegistry = new AdapterRegistry(new ISiteAdapter[]
    {
        new YouTubeAdapter(),
        new FacebookAdapter()
    });

    var engine = new HybridDownloadEngine(new DirectDownloadEngine(), new YtDlpDownloadEngine());
    var compliance = new ComplianceValidator(new[] { "youtube", "facebook" });
    return new DownloadCoordinator(adapterRegistry, engine, compliance);
});

builder.Services.AddSingleton<DownloadSessionStore>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () =>
{
    var hostedMode = IsHostedMode();
    var serverOutputFolder = ResolveOutputPath(null);
    return Results.Ok(new
    {
        ok = true,
        runtime = Environment.Version.ToString(),
        hostedMode,
        serverOutputFolder
    });
});

app.MapPost("/api/probe", async (ProbeUiRequest request, DownloadCoordinator coordinator) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
    {
        return Results.BadRequest(new { ok = false, error = "Invalid URL." });
    }

    var result = await coordinator.DetectAsync(new PageContext(uri, request.PageTitle), CancellationToken.None);
    if (!result.IsSupported || result.MediaInfo is null)
    {
        return Results.BadRequest(new { ok = false, error = result.UnsupportedReason ?? "Unsupported or blocked." });
    }

    return Results.Ok(new { ok = true, site = result.Site, mediaInfo = result.MediaInfo });
});

app.MapPost("/api/pick-folder", async (PickFolderUiRequest request) =>
{
    var pick = await TryPickFolderAsync(request.InitialPath);
    if (!pick.Success)
    {
        return Results.BadRequest(new { ok = false, error = pick.Error ?? "Folder selection failed." });
    }

    return Results.Ok(new { ok = true, path = pick.Path });
});

app.MapPost("/api/download-start", (DownloadUiRequest request, DownloadCoordinator coordinator, DownloadSessionStore store) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
    {
        return Results.BadRequest(new { ok = false, error = "Invalid URL." });
    }

    var outputPath = ResolveOutputPath(request.OutputPath);
    Directory.CreateDirectory(outputPath);

    var sessionId = Guid.NewGuid().ToString("N");
    var session = store.Create(sessionId);

    _ = Task.Run(async () =>
    {
        try
        {
            session.State = "Starting";
            session.Logs.Enqueue("[Pending] Preparing download");
            Console.WriteLine($"[UI-DL {sessionId}] Starting url={request.Url} format={request.SelectedFormatId}");

            var downloadRequest = new DownloadRequest(
                SourceUrl: uri,
                Site: request.Site,
                SelectedFormatId: string.IsNullOrWhiteSpace(request.SelectedFormatId) ? "best" : request.SelectedFormatId,
                OutputPath: outputPath,
                FilenameTemplate: string.IsNullOrWhiteSpace(request.FilenameTemplate) ? "video" : request.FilenameTemplate);

            var progress = new Progress<DownloadProgress>(p =>
            {
                session.Percent = Math.Clamp(p.Percent, 0, 100);
                session.State = p.State.ToString();
                session.Status = p.Status;
                session.Logs.Enqueue($"[{p.State}] {p.Status} {(p.Percent > 0 ? $"{p.Percent:0.0}%" : "")}".Trim());
                session.TrimLogs();
                Console.WriteLine($"[UI-DL {sessionId}] {session.State} {session.Status} {session.Percent:0.0}%");
            });

            var handle = await coordinator.StartDownloadAsync(downloadRequest, progress, CancellationToken.None);
            session.DownloadId = handle.DownloadId;

            await handle.Completion;

            session.Files = handle.Artifacts?.ToArray() ?? Array.Empty<string>();
            session.Percent = 100;
            session.State = "Completed";
            session.Status = "Completed";
            session.Logs.Enqueue("[Completed] Download finished");
            session.TrimLogs();
            Console.WriteLine($"[UI-DL {sessionId}] Completed");
        }
        catch (Exception ex)
        {
            var friendly = BuildFriendlyDownloadError(ex.Message, session.Logs);
            session.State = "Failed";
            session.Status = friendly;
            session.Error = friendly;
            session.Logs.Enqueue($"[Failed] {friendly}");
            session.TrimLogs();
            Console.WriteLine($"[UI-DL {sessionId}] Failed: {friendly}");
        }
    });

    return Results.Ok(new { ok = true, sessionId });
});

app.MapGet("/api/download-status/{sessionId}", (string sessionId, DownloadSessionStore store) =>
{
    var session = store.Get(sessionId);
    if (session is null)
    {
        return Results.NotFound(new { ok = false, error = "Session not found." });
    }

    return Results.Ok(new
    {
        ok = true,
        state = session.State,
        status = session.Status,
        percent = session.Percent,
        error = session.Error,
        files = session.Files,
        logs = session.Logs.ToArray()
    });
});

var displayUrl = string.IsNullOrWhiteSpace(port) ? "http://127.0.0.1:5077" : $"http://0.0.0.0:{port}";
Console.WriteLine($"Web UI running at {displayUrl}");
Console.WriteLine("Open this URL in your browser.");
Console.WriteLine(debugLogsEnabled
    ? "UI_DEBUG=1 enabled: ASP.NET request logs are verbose."
    : "Tip: run with UI_DEBUG=1 to see verbose ASP.NET request logs.");

app.Run();

static bool IsHostedMode()
{
    return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VERCEL"));
}

static string ResolveOutputPath(string? requestedPath)
{
    if (IsHostedMode())
    {
        return Path.Combine(Path.GetTempPath(), "authorized-downloader");
    }

    if (!string.IsNullOrWhiteSpace(requestedPath))
    {
        try
        {
            var full = Path.GetFullPath(requestedPath);
            if (Directory.Exists(full))
            {
                return full;
            }
        }
        catch
        {
            // Fall through to default.
        }
    }

    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrWhiteSpace(userProfile))
    {
        return Path.Combine(userProfile, "Downloads");
    }

    return Path.Combine(Path.GetTempPath(), "authorized-downloader");
}

static string BuildFriendlyDownloadError(string rawMessage, ConcurrentQueue<string> logs)
{
    var lines = logs.ToArray();
    var joined = string.Join('\n', lines).ToLowerInvariant();
    if (joined.Contains("sign in to confirm you’re not a bot")
        || joined.Contains("sign in to confirm you're not a bot"))
    {
        return "Download blocked by YouTube anti-bot/auth checks on cloud hosting. Use local desktop/host mode for this video.";
    }

    if (joined.Contains("no supported javascript runtime could be found"))
    {
        return "Download engine missing JavaScript runtime support on server.";
    }

    return rawMessage;
}

static async Task<PickFolderResult> TryPickFolderAsync(string? initialPath)
{
    try
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new PickFolderResult(false, null, "Folder picker currently implemented for macOS only.");
        }

        var escaped = EscapeAppleScriptString(initialPath);
        var script = string.IsNullOrWhiteSpace(escaped)
            ? "POSIX path of (choose folder with prompt \"Select download folder\")"
            : $"POSIX path of (choose folder with prompt \"Select download folder\" default location POSIX file \"{escaped}\")";

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new PickFolderResult(false, null, "Failed to start folder picker.");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            return new PickFolderResult(false, null, string.IsNullOrWhiteSpace(stderr) ? "Folder selection cancelled." : stderr.Trim());
        }

        var path = stdout.Trim();
        return string.IsNullOrWhiteSpace(path)
            ? new PickFolderResult(false, null, "No folder selected.")
            : new PickFolderResult(true, path, null);
    }
    catch (Exception ex)
    {
        return new PickFolderResult(false, null, ex.Message);
    }
}

static string EscapeAppleScriptString(string? value)
{
    return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}

internal sealed record ProbeUiRequest(string Url, string? PageTitle);
internal sealed record PickFolderUiRequest(string? InitialPath);
internal sealed record DownloadUiRequest(string Url, string Site, string SelectedFormatId, string? OutputPath, string FilenameTemplate);
internal sealed record PickFolderResult(bool Success, string? Path, string? Error);

internal sealed class DownloadSessionStore
{
    private readonly ConcurrentDictionary<string, DownloadSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public DownloadSession Create(string id)
    {
        var session = new DownloadSession();
        _sessions[id] = session;
        return session;
    }

    public DownloadSession? Get(string id)
    {
        _sessions.TryGetValue(id, out var session);
        return session;
    }
}

internal sealed class DownloadSession
{
    public string State { get; set; } = "Queued";
    public string Status { get; set; } = "Waiting";
    public double Percent { get; set; }
    public string? Error { get; set; }
    public string? DownloadId { get; set; }
    public string[] Files { get; set; } = Array.Empty<string>();
    public ConcurrentQueue<string> Logs { get; } = new();

    public void TrimLogs()
    {
        while (Logs.Count > 120 && Logs.TryDequeue(out _))
        {
        }
    }
}
