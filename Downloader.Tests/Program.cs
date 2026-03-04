using Downloader.Core.Adapters;
using Downloader.Core.Compliance;
using Downloader.Core.Contracts;
using Downloader.Core.Native;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("YouTube adapter matches valid host", TestYouTubeHostMatchAsync),
    ("Facebook adapter rejects unrelated host", TestFacebookRejectAsync),
    ("Compliance blocks DRM", TestComplianceBlocksDrmAsync),
    ("Native message framing roundtrip", TestNativeFramingRoundtripAsync)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static Task TestYouTubeHostMatchAsync()
{
    var adapter = new YouTubeAdapter();
    if (!adapter.CanHandle(new Uri("https://www.youtube.com/watch?v=abc")))
    {
        throw new InvalidOperationException("Expected YouTube host to match.");
    }

    return Task.CompletedTask;
}

static Task TestFacebookRejectAsync()
{
    var adapter = new FacebookAdapter();
    if (adapter.CanHandle(new Uri("https://example.com/video")))
    {
        throw new InvalidOperationException("Unexpected match for unsupported host.");
    }

    return Task.CompletedTask;
}

static Task TestComplianceBlocksDrmAsync()
{
    var validator = new ComplianceValidator(new[] { "youtube" });
    var media = new MediaInfo(
        Title: "x",
        ThumbnailUrl: null,
        Duration: null,
        Formats: Array.Empty<DownloadFormat>(),
        HasAudio: false,
        HasVideo: true,
        Restrictions: new Restrictions(true, false, "drm", "DRM"));

    var result = validator.ValidateProbe("youtube", media);
    if (result.Allowed)
    {
        throw new InvalidOperationException("DRM media should be blocked.");
    }

    return Task.CompletedTask;
}

static async Task TestNativeFramingRoundtripAsync()
{
    await using var stream = new MemoryStream();
    await NativeMessageFraming.WriteMessageAsync(stream, "hello", CancellationToken.None);
    stream.Position = 0;
    var payload = await NativeMessageFraming.ReadMessageAsync(stream, CancellationToken.None);

    if (payload != "hello")
    {
        throw new InvalidOperationException($"Unexpected payload: {payload}");
    }
}
