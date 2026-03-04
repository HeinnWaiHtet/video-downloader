using Downloader.Core.Adapters;
using Downloader.Core.Compliance;
using Downloader.Core.Contracts;
using Downloader.Core.Engines;
using Downloader.Core.Interfaces;
using Downloader.Core.Services;
#if MACCATALYST
using Foundation;
using UniformTypeIdentifiers;
using UIKit;
#endif

namespace Downloader.Desktop;

public partial class MainPage : ContentPage
{
    private readonly DownloadCoordinator _coordinator;
    private readonly List<FormatChoice> _formatChoices = new();
    private string? _site;
    private string _lastLoadedUrl = string.Empty;
    private bool _isLoadingVideo;

    public MainPage()
    {
        InitializeComponent();

        var adapters = new AdapterRegistry(new ISiteAdapter[]
        {
            new YouTubeAdapter(),
            new FacebookAdapter()
        });
        var engine = new HybridDownloadEngine(new DirectDownloadEngine(), new YtDlpDownloadEngine());
        var compliance = new ComplianceValidator(new[] { "youtube", "facebook" });
        _coordinator = new DownloadCoordinator(adapters, engine, compliance);

        QualityPicker.ItemsSource = null;
        PopulateFormats(null);
        QualityPicker.SelectedIndex = 0;

        FolderEntry.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        NameEntry.Text = "video";
        LogsEditor.Text = string.Empty;
    }

    private async void OnUrlEntryUnfocused(object? sender, FocusEventArgs e)
    {
        await LoadVideoAsync();
    }

    private async void OnRootTapped(object? sender, TappedEventArgs e)
    {
        UrlEntry.Unfocus();
        await LoadVideoAsync();
    }

    private async Task LoadVideoAsync()
    {
        var rawUrl = UrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return;
        }

        if (_isLoadingVideo || string.Equals(_lastLoadedUrl, rawUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var url))
        {
            ProbeStatusLabel.Text = "Invalid URL.";
            ProbeStatusLabel.TextColor = Colors.IndianRed;
            return;
        }

        _isLoadingVideo = true;
        SetBusy(true, "Probing video...");

        try
        {
            var result = await _coordinator.DetectAsync(new PageContext(url, null), CancellationToken.None);
            if (!result.IsSupported || result.MediaInfo is null)
            {
                ProbeStatusLabel.Text = $"Blocked/unsupported: {result.UnsupportedReason ?? "unknown"}";
                ProbeStatusLabel.TextColor = Colors.IndianRed;
                return;
            }

            _site = result.Site;
            PopulateFormats(result.MediaInfo.Formats);

            QualityPicker.SelectedIndex = 0;
            NameEntry.Text = SanitizeName(result.MediaInfo.Title);
            ProbeStatusLabel.Text = $"Ready: {result.MediaInfo.Title}";
            ProbeStatusLabel.TextColor = Colors.ForestGreen;
            _lastLoadedUrl = rawUrl;
        }
        catch (Exception ex)
        {
            ProbeStatusLabel.Text = ex.Message;
            ProbeStatusLabel.TextColor = Colors.IndianRed;
        }
        finally
        {
            SetBusy(false, "Idle");
            _isLoadingVideo = false;
        }
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        _site = null;
        _lastLoadedUrl = string.Empty;
        UrlEntry.Text = string.Empty;
        NameEntry.Text = string.Empty;
        FolderEntry.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        ProbeStatusLabel.Text = "Ready";
        ProbeStatusLabel.TextColor = Colors.DarkSlateGray;
        DownloadStatusLabel.Text = "Idle";
        DownloadStatusLabel.TextColor = Colors.DarkSlateGray;
        DownloadProgressBar.Progress = 0;
        LogsEditor.Text = string.Empty;
        PopulateFormats(null);
        QualityPicker.SelectedIndex = 0;
        SetBusy(false, "Idle");
    }

    private async void OnBrowseClicked(object? sender, EventArgs e)
    {
        var result = await PickFolderAsync(FolderEntry.Text);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Path))
        {
            FolderEntry.Text = result.Path;
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            await DisplayAlert("Folder Picker", result.Error, "OK");
        }
    }

    private async void OnDownloadClicked(object? sender, EventArgs e)
    {
        var rawUrl = UrlEntry.Text?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var url))
        {
            await DisplayAlert("Download", "Invalid URL", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_site))
        {
            await LoadVideoAsync();
            if (string.IsNullOrWhiteSpace(_site))
            {
                await DisplayAlert("Download", "Video not loaded. Please check URL.", "OK");
                return;
            }
        }

        var selectedLabel = QualityPicker.SelectedItem?.ToString();
        var formatId = _formatChoices.FirstOrDefault(f => string.Equals(f.Label, selectedLabel, StringComparison.Ordinal))?.Id ?? "best";
        var outputFolder = string.IsNullOrWhiteSpace(FolderEntry.Text)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : FolderEntry.Text.Trim();
        var fileName = string.IsNullOrWhiteSpace(NameEntry.Text) ? "video" : NameEntry.Text.Trim();

        SetBusy(true, "Starting download...");
        DownloadProgressBar.Progress = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var normalized = Math.Clamp(p.Percent / 100.0, 0.0, 1.0);
                DownloadProgressBar.Progress = normalized;
                DownloadStatusLabel.Text = $"{p.State}: {p.Status} {(p.Percent > 0 ? $"{p.Percent:0.0}%" : string.Empty)}";
                DownloadStatusLabel.TextColor = p.State == DownloadState.Failed ? Colors.IndianRed : Colors.DarkSlateGray;
                AppendLog($"[{p.State}] {p.Status} {(p.Percent > 0 ? $"{p.Percent:0.0}%" : string.Empty)}".Trim());
            });
        });

        try
        {
            var request = new DownloadRequest(
                SourceUrl: url,
                Site: _site,
                SelectedFormatId: formatId,
                OutputPath: outputFolder,
                FilenameTemplate: fileName);

            var handle = await _coordinator.StartDownloadAsync(request, progress, CancellationToken.None);
            await handle.Completion;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                DownloadProgressBar.Progress = 1;
                DownloadStatusLabel.Text = "Completed";
                DownloadStatusLabel.TextColor = Colors.ForestGreen;
            });

            var savedTarget = handle.Artifacts is { Count: > 0 } ? string.Join(Environment.NewLine, handle.Artifacts) : outputFolder;
            AppendLog($"[Completed] Saved to: {savedTarget}");
            await DisplayAlert("Success", $"Download completed.\n{savedTarget}", "OK");
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DownloadStatusLabel.Text = ex.Message;
                DownloadStatusLabel.TextColor = Colors.IndianRed;
            });
            AppendLog($"[Failed] {ex.Message}");
            await DisplayAlert("Download Failed", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false, DownloadStatusLabel.Text);
        }
    }

    private void SetBusy(bool isBusy, string status)
    {
        ClearButton.IsEnabled = !isBusy;
        DownloadButton.IsEnabled = !isBusy;
        BrowseButton.IsEnabled = !isBusy;
        DownloadStatusLabel.Text = status;
        if (!isBusy && DownloadStatusLabel.TextColor != Colors.IndianRed)
        {
            DownloadStatusLabel.TextColor = Colors.DarkSlateGray;
        }
    }

    private void AppendLog(string line)
    {
        var current = LogsEditor.Text ?? string.Empty;
        if (current.Length > 8000)
        {
            current = current[^6000..];
        }

        LogsEditor.Text = string.IsNullOrWhiteSpace(current)
            ? line
            : current + Environment.NewLine + line;
    }

    private static async Task<FolderPickResult> PickFolderAsync(string? initialPath)
    {
        _ = initialPath;
        
#if MACCATALYST
        try
        {
            return await PickFolderMacCatalystAsync();
        }
        catch
        {
            return new FolderPickResult(false, null, "Could not open folder picker. Please type folder path manually.");
        }
#else
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select any file inside your target folder"
            });

            if (picked is null)
            {
                return new FolderPickResult(false, null, null);
            }

            var path = Path.GetDirectoryName(picked.FullPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return new FolderPickResult(false, null, "Could not resolve selected folder.");
            }

            return new FolderPickResult(true, path, null);
        }
        catch (OperationCanceledException)
        {
            return new FolderPickResult(false, null, null);
        }
        catch
        {
            return new FolderPickResult(false, null, "Could not open folder picker. Please type folder path manually.");
        }
#endif
    }

    private static string SanitizeName(string value)
    {
        var cleaned = value;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(cleaned) ? "video" : cleaned;
    }

    private sealed class FormatChoice
    {
        public FormatChoice(string id, string label)
        {
            Id = id;
            Label = string.IsNullOrWhiteSpace(label) ? id : label;
        }

        public string Id { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }

    private void PopulateFormats(IReadOnlyList<DownloadFormat>? detected)
    {
        _formatChoices.Clear();
        AddFormatIfMissing("best", "Best available");
        AddFormatIfMissing("1080p", "1080p");
        AddFormatIfMissing("720p", "720p");
        AddFormatIfMissing("audio", "Audio only");

        if (detected is null)
        {
            return;
        }

        foreach (var format in detected)
        {
            AddFormatIfMissing(format.Id, format.Label);
        }

        QualityPicker.ItemsSource = _formatChoices.Select(x => x.Label).ToList();
    }

    private void AddFormatIfMissing(string id, string label)
    {
        if (_formatChoices.Any(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _formatChoices.Add(new FormatChoice(id, label));
    }

    private sealed record FolderPickResult(bool Success, string? Path, string? Error);

#if MACCATALYST
    private static Task<FolderPickResult> PickFolderMacCatalystAsync()
    {
        var tcs = new TaskCompletionSource<FolderPickResult>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var picker = new UIDocumentPickerViewController(new[] { UTTypes.Folder }, asCopy: false);
                picker.AllowsMultipleSelection = false;

                var del = new DocumentPickerDelegate(
                    onPicked: urls =>
                    {
                        var path = urls?.FirstOrDefault()?.Path;
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            tcs.TrySetResult(new FolderPickResult(false, null, "Could not resolve selected folder."));
                            return;
                        }

                        tcs.TrySetResult(new FolderPickResult(true, path, null));
                    },
                    onCancelled: () => tcs.TrySetResult(new FolderPickResult(false, null, null)));

                picker.Delegate = del;

                var windowScene = UIApplication.SharedApplication
                    .ConnectedScenes
                    .OfType<UIWindowScene>()
                    .FirstOrDefault();
                var rootController = windowScene?
                    .Windows
                    .FirstOrDefault(w => w.IsKeyWindow)?
                    .RootViewController;

                if (rootController is null)
                {
                    tcs.TrySetResult(new FolderPickResult(false, null, "Could not open folder picker window."));
                    return;
                }

                rootController.PresentViewController(picker, true, null);
            }
            catch
            {
                tcs.TrySetResult(new FolderPickResult(false, null, "Could not open folder picker."));
            }
        });

        return tcs.Task;
    }

    private sealed class DocumentPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly Action<NSUrl[]?> _onPicked;
        private readonly Action _onCancelled;

        public DocumentPickerDelegate(Action<NSUrl[]?> onPicked, Action onCancelled)
        {
            _onPicked = onPicked;
            _onCancelled = onCancelled;
        }

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            _onPicked(urls);
        }

        public override void WasCancelled(UIDocumentPickerViewController controller)
        {
            _onCancelled();
        }
    }
#endif
}
