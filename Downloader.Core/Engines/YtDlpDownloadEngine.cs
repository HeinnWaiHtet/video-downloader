using System.Diagnostics;
using System.Globalization;
using Downloader.Core.Contracts;
using Downloader.Core.Interfaces;

namespace Downloader.Core.Engines;

public sealed class YtDlpDownloadEngine : IDownloadEngine
{
    private readonly string _ytDlpExecutable;
    private readonly string _workingDirectory;

    public YtDlpDownloadEngine(string ytDlpExecutable = "yt-dlp")
    {
        _ytDlpExecutable = ResolveExecutable(ytDlpExecutable);
        _workingDirectory = ResolveWorkingDirectory();
    }

    public async Task<MediaInfo> ProbeAsync(PageContext context, CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync($"--no-playlist --skip-download --dump-single-json \"{context.SourceUrl}\"", cancellationToken);
        if (output.ExitCode != 0)
        {
            return MediaInfo.Blocked("yt_dlp_probe_failed", output.Stderr.Trim());
        }

        var title = TryExtractJsonString(output.Stdout, "title") ?? context.PageTitle ?? "Video";
        var formats = new List<DownloadFormat>
        {
            new("best", "Best available", "mp4", null, true, true),
            new("bv*+ba/b", "Best video + audio", "mp4", null, true, true),
            new("ba", "Best audio", "m4a", null, true, false)
        };

        return new MediaInfo(title, null, null, formats, true, true, Restrictions.None);
    }

    public Task<DownloadHandle> StartAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(request.OutputPath);

        var safeName = BuildSafeFileName(request.FilenameTemplate);
        var outputTemplate = Path.Combine(request.OutputPath, $"{safeName}.%(ext)s");
        var format = ResolveFormatExpression(request.SelectedFormatId);

        var args = $"--no-playlist --newline --progress --force-overwrites --no-part -f \"{format}\" -o \"{outputTemplate}\" \"{request.SourceUrl}\"";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ytDlpExecutable,
                Arguments = args,
                WorkingDirectory = _workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            process.Start();
            process.StandardInput.Close();
        }
        catch
        {
            process.Dispose();
            throw new InvalidOperationException("Could not start download engine process. Ensure yt-dlp is installed and allowed on this system.");
        }
        progress.Report(new DownloadProgress(id, DownloadState.Downloading, 0, "yt-dlp started"));

        var readOut = ReadProgressLinesAsync(process.StandardOutput, id, progress, cancellationToken);
        var readErr = ReadProgressLinesAsync(process.StandardError, id, progress, cancellationToken);

        var completion = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(readOut, readErr, process.WaitForExitAsync(cancellationToken));
                if (process.ExitCode == 0)
                {
                    progress.Report(new DownloadProgress(id, DownloadState.Completed, 100, "Completed"));
                    return;
                }

                progress.Report(new DownloadProgress(id, DownloadState.Failed, 0, $"yt-dlp failed ({process.ExitCode})"));
                throw new InvalidOperationException($"yt-dlp failed: {process.ExitCode}");
            }
            finally
            {
                process.Dispose();
            }
        }, cancellationToken);

        return Task.FromResult(
            new DownloadHandle(
                id,
                completion,
                () => SafeKill(process),
                new System.Collections.ObjectModel.ReadOnlyCollection<string>(new[] { request.OutputPath })));
    }

    private async Task<ProcessResult> RunProcessAsync(string args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ytDlpExecutable,
                Arguments = args,
                WorkingDirectory = _workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            process.StandardInput.Close();
        }
        catch
        {
            return new ProcessResult(1, string.Empty, "Could not start yt-dlp process.");
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task ReadProgressLinesAsync(
        StreamReader reader,
        string downloadId,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                continue;
            }

            var percent = ParsePercent(line);
            progress.Report(new DownloadProgress(downloadId, DownloadState.Downloading, percent ?? 0, line.Trim()));
        }
    }

    private static double? ParsePercent(string line)
    {
        var marker = "%";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index <= 0)
        {
            return null;
        }

        var start = index - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.'))
        {
            start--;
        }

        var segment = line[(start + 1)..index];
        if (double.TryParse(segment, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Clamp(value, 0, 100);
        }

        return null;
    }

    private static string? TryExtractJsonString(string json, string key)
    {
        var token = $"\"{key}\": \"";
        var index = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        index += token.Length;
        var end = json.IndexOf('"', index);
        return end > index ? json[index..end] : null;
    }

    private static void SafeKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string ResolveWorkingDirectory()
    {
        var current = Environment.CurrentDirectory;
        if (Directory.Exists(current))
        {
            return current;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
        {
            return home;
        }

        return Path.GetTempPath();
    }

    private static string ResolveExecutable(string configured)
    {
        if (Path.IsPathRooted(configured) && File.Exists(configured))
        {
            return configured;
        }

        var fromEnv = Environment.GetEnvironmentVariable("YTDLP_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var commonPaths = new[]
        {
            "/opt/homebrew/bin/yt-dlp",
            "/usr/local/bin/yt-dlp",
            "/usr/bin/yt-dlp"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return configured;
    }

    private static string BuildSafeFileName(string? input)
    {
        var value = string.IsNullOrWhiteSpace(input) ? "video" : input.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "video" : value;
    }

    private static string ResolveFormatExpression(string? selectedFormatId)
    {
        var key = string.IsNullOrWhiteSpace(selectedFormatId)
            ? "best"
            : selectedFormatId.Trim().ToLowerInvariant();

        return key switch
        {
            // Prefer single-file outputs to avoid separate .fXXX video/audio files.
            "best" => "best[ext=mp4]/best",
            "1080p" => "best[height<=1080][ext=mp4]/best[height<=1080]/best[ext=mp4]/best",
            "720p" => "best[height<=720][ext=mp4]/best[height<=720]/best[ext=mp4]/best",
            "audio" => "bestaudio[ext=m4a]/bestaudio",
            _ => selectedFormatId!.Trim()
        };
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
