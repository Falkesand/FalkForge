# Update Feeds: Background Download & Launch — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Complete `DownloadAndPrompt` and `AutoUpdate` policies with background download, percentage progress, resume support, and update launch.

**Architecture:** A new `UpdateDownloader` class orchestrates background download as a fire-and-forget `Task` started at the end of `DetectingHandler`. Progress is reported via a new protocol message. On completion, `UpdateReadyMessage` is sent; for `AutoUpdate` the engine self-launches the new EXE and shuts down; for `DownloadAndPrompt` the UI prompts the user who sends a `LaunchUpdateMessage` back.

**Tech Stack:** .NET 10, C# latest, NativeAOT constraints (no reflection), binary protocol (MessageSerializer/Deserializer), `HttpClient`, xUnit 2.9.3, `Result<T>`.

**Design doc:** `docs/plans/2026-02-25-update-feeds-download-design.md`

---

## Task 1: Protocol — New Message Types

**Files:**
- Modify: `src/FalkForge.Engine.Protocol/MessageType.cs`
- Create: `src/FalkForge.Engine.Protocol/Messages/UpdateDownloadProgressMessage.cs`
- Create: `src/FalkForge.Engine.Protocol/Messages/LaunchUpdateMessage.cs`
- Modify: `src/FalkForge.Engine.Protocol/Serialization/MessageSerializer.cs`
- Modify: `src/FalkForge.Engine.Protocol/Serialization/MessageDeserializer.cs`
- Test: `tests/FalkForge.Engine.Protocol.Tests/Serialization/MessageSerializerTests.cs`

**Step 1: Write the failing tests**

Add to `MessageSerializerTests.cs`:
```csharp
[Fact]
public void RoundTrip_UpdateDownloadProgressMessage_PreservesAllFields()
{
    var msg = new UpdateDownloadProgressMessage
    {
        SequenceId = 42,
        BytesReceived = 500_000,
        TotalBytes = 2_000_000,
        PercentComplete = 25
    };
    var bytes = MessageSerializer.Serialize(msg);
    var result = MessageDeserializer.Deserialize(bytes);
    var deserialized = Assert.IsType<UpdateDownloadProgressMessage>(result);
    Assert.Equal(42, deserialized.SequenceId);
    Assert.Equal(500_000, deserialized.BytesReceived);
    Assert.Equal(2_000_000, deserialized.TotalBytes);
    Assert.Equal(25, deserialized.PercentComplete);
}

[Fact]
public void RoundTrip_UpdateDownloadProgressMessage_UnknownSize_TotalBytesIsNegativeOne()
{
    var msg = new UpdateDownloadProgressMessage
    {
        SequenceId = 1,
        BytesReceived = 81_920,
        TotalBytes = -1,
        PercentComplete = 0
    };
    var bytes = MessageSerializer.Serialize(msg);
    var result = MessageDeserializer.Deserialize(bytes);
    var deserialized = Assert.IsType<UpdateDownloadProgressMessage>(result);
    Assert.Equal(-1, deserialized.TotalBytes);
    Assert.Equal(0, deserialized.PercentComplete);
}

[Fact]
public void RoundTrip_LaunchUpdateMessage_PreservesSequenceId()
{
    var msg = new LaunchUpdateMessage { SequenceId = 99 };
    var bytes = MessageSerializer.Serialize(msg);
    var result = MessageDeserializer.Deserialize(bytes);
    var deserialized = Assert.IsType<LaunchUpdateMessage>(result);
    Assert.Equal(99, deserialized.SequenceId);
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Protocol.Tests/ -v --filter "RoundTrip_UpdateDownloadProgress|RoundTrip_LaunchUpdate"
```
Expected: compile error (types don't exist yet).

**Step 3: Add enum values to `MessageType.cs`**

Add after the existing `UpdateReady` entry:
```csharp
UpdateDownloadProgress = 0x010E,
LaunchUpdate = 0x010F,
```

**Step 4: Create `UpdateDownloadProgressMessage.cs`**
```csharp
namespace FalkForge.Engine.Protocol.Messages;

public sealed class UpdateDownloadProgressMessage : EngineMessage
{
    public override MessageType Type => MessageType.UpdateDownloadProgress;
    public required long BytesReceived { get; init; }
    public required long TotalBytes { get; init; }
    public required int PercentComplete { get; init; }
}
```

**Step 5: Create `LaunchUpdateMessage.cs`**
```csharp
namespace FalkForge.Engine.Protocol.Messages;

public sealed class LaunchUpdateMessage : EngineMessage
{
    public override MessageType Type => MessageType.LaunchUpdate;
}
```

**Step 6: Update `MessageSerializer.cs`**

Add cases for the two new types following the same pattern as `UpdateAvailableMessage`:
```csharp
UpdateDownloadProgressMessage m => SerializeUpdateDownloadProgress(writer, m),
LaunchUpdateMessage => { /* no payload */ },
```

Add serializer method:
```csharp
private static void SerializeUpdateDownloadProgress(BinaryWriter w, UpdateDownloadProgressMessage m)
{
    w.Write(m.BytesReceived);
    w.Write(m.TotalBytes);
    w.Write(m.PercentComplete);
}
```

**Step 7: Update `MessageDeserializer.cs`**

Add cases:
```csharp
MessageType.UpdateDownloadProgress => new UpdateDownloadProgressMessage
{
    SequenceId = sequenceId,
    BytesReceived = reader.ReadInt64(),
    TotalBytes = reader.ReadInt64(),
    PercentComplete = reader.ReadInt32()
},
MessageType.LaunchUpdate => new LaunchUpdateMessage { SequenceId = sequenceId },
```

**Step 8: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Protocol.Tests/ -v --filter "RoundTrip_UpdateDownloadProgress|RoundTrip_LaunchUpdate"
```
Expected: 3 tests PASS.

**Step 9: Full build — zero warnings**
```bash
dotnet build
```

**Step 10: Commit**
```bash
git add src/FalkForge.Engine.Protocol/ tests/FalkForge.Engine.Protocol.Tests/
git commit -m "feat: add UpdateDownloadProgress and LaunchUpdate protocol messages"
```

---

## Task 2: Resume Config — `UpdateFeedConfig` + `ManifestUpdateFeed` + `ManifestGenerator`

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/UpdateFeedConfig.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs`
- Modify: `src/FalkForge.Engine.Protocol/Manifest/ManifestUpdateFeed.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs`
- Test: `tests/FalkForge.Compiler.Bundle.Tests/Builders/BundleBuilderTests.cs`
- Test: `tests/FalkForge.Compiler.Bundle.Tests/Compilation/ManifestGeneratorTests.cs`

**Step 1: Write the failing tests**

In `BundleBuilderTests.cs`:
```csharp
[Fact]
public void UpdateFeed_DefaultAllowResume_IsTrue()
{
    var model = new BundleBuilder()
        .UpdateFeed("https://example.com/feed.json")
        .Build();
    Assert.True(model.UpdateFeed!.AllowResumeDownload);
}

[Fact]
public void UpdateFeed_AllowResumeDisabled_StoredOnModel()
{
    var model = new BundleBuilder()
        .UpdateFeed("https://example.com/feed.json", allowResume: false)
        .Build();
    Assert.False(model.UpdateFeed!.AllowResumeDownload);
}
```

In `ManifestGeneratorTests.cs`:
```csharp
[Fact]
public void Generate_MapsAllowResumeDownload_True()
{
    var model = BundleModelFactory.WithUpdateFeed(allowResume: true);
    var manifest = ManifestGenerator.Generate(model);
    Assert.True(manifest.UpdateFeed!.AllowResumeDownload);
}

[Fact]
public void Generate_MapsAllowResumeDownload_False()
{
    var model = BundleModelFactory.WithUpdateFeed(allowResume: false);
    var manifest = ManifestGenerator.Generate(model);
    Assert.False(manifest.UpdateFeed!.AllowResumeDownload);
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Compiler.Bundle.Tests/ -v --filter "AllowResume"
```
Expected: compile errors.

**Step 3: Add `AllowResumeDownload` to `UpdateFeedConfig.cs`**
```csharp
public bool AllowResumeDownload { get; init; } = true;
```

**Step 4: Update `BundleBuilder.UpdateFeed()` signature**
```csharp
public BundleBuilder UpdateFeed(
    string feedUrl,
    UpdatePolicy policy = UpdatePolicy.NotifyOnly,
    bool allowResume = true)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);
    _updateFeed = new UpdateFeedConfig
    {
        FeedUrl = feedUrl,
        Policy = policy,
        AllowResumeDownload = allowResume
    };
    return this;
}
```

**Step 5: Update `ManifestUpdateFeed.cs`**
```csharp
public sealed record ManifestUpdateFeed(string FeedUrl, UpdatePolicy Policy, bool AllowResumeDownload);
```

**Step 6: Update `ManifestGenerator.cs`**

In the mapping section, update `ManifestUpdateFeed` construction to include `AllowResumeDownload`:
```csharp
ManifestUpdateFeed? updateFeed = model.UpdateFeed is not null
    ? new ManifestUpdateFeed(
        model.UpdateFeed.FeedUrl,
        model.UpdateFeed.Policy,
        model.UpdateFeed.AllowResumeDownload)
    : null;
```

**Step 7: Fix any call sites that construct `ManifestUpdateFeed` with the old 2-arg form**
```bash
dotnet build 2>&1 | grep "ManifestUpdateFeed"
```
Fix any remaining compile errors.

**Step 8: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Compiler.Bundle.Tests/ -v --filter "AllowResume"
```
Expected: 4 tests PASS.

**Step 9: Full build and full test suite**
```bash
dotnet build && dotnet test
```
Expected: 0 warnings, all tests pass.

**Step 10: Commit**
```bash
git add src/FalkForge.Compiler.Bundle/ src/FalkForge.Engine.Protocol/Manifest/ManifestUpdateFeed.cs tests/FalkForge.Compiler.Bundle.Tests/
git commit -m "feat: add AllowResumeDownload config to UpdateFeedConfig and ManifestUpdateFeed"
```

---

## Task 3: `PayloadDownloader` — Progress Callback

**Files:**
- Modify: `src/FalkForge.Engine/Download/PayloadDownloader.cs`
- Modify: `tests/FalkForge.Engine.Tests/Download/PayloadDownloaderTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task DownloadAsync_ReportsProgressPerChunk()
{
    // Arrange — 200KB payload (should produce 2+ progress reports with 81KB chunks)
    var content = new byte[200_000];
    Random.Shared.NextBytes(content);
    var sha256 = ComputeSha256(content);
    var handler = new FakeHttpMessageHandler(content, includeContentLength: true);
    var downloader = new PayloadDownloader(new HttpClient(handler));
    var progressReports = new List<(long bytes, long total)>();
    var progress = new Progress<(long bytes, long total)>(p => progressReports.Add(p));
    var dest = Path.GetTempFileName();

    try
    {
        // Act
        var result = await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, dest, progress, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(progressReports);
        Assert.Equal(content.Length, progressReports.Last().bytes);
        Assert.Equal(content.Length, progressReports.Last().total);
        Assert.All(progressReports, r => Assert.True(r.bytes <= r.total));
    }
    finally { File.Delete(dest); }
}

[Fact]
public async Task DownloadAsync_UnknownContentLength_ReportsTotalAsNegativeOne()
{
    var content = new byte[100_000];
    var sha256 = ComputeSha256(content);
    var handler = new FakeHttpMessageHandler(content, includeContentLength: false);
    var downloader = new PayloadDownloader(new HttpClient(handler));
    var progressReports = new List<(long bytes, long total)>();
    var progress = new Progress<(long bytes, long total)>(p => progressReports.Add(p));
    var dest = Path.GetTempFileName();

    try
    {
        await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, dest, progress, CancellationToken.None);
        Assert.All(progressReports, r => Assert.Equal(-1, r.total));
    }
    finally { File.Delete(dest); }
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "ReportsProgress|UnknownContentLength"
```
Expected: compile error (`DownloadAsync` lacks `progress` parameter).

**Step 3: Update `PayloadDownloader.DownloadAsync` signature and implementation**

Add parameter:
```csharp
internal async Task<Result<string>> DownloadAsync(
    string url,
    string sha256,
    string destPath,
    IProgress<(long BytesReceived, long TotalBytes)>? progress,
    CancellationToken ct)
```

In the streaming loop, after writing each chunk to disk:
```csharp
totalRead += bytesRead;
progress?.Report((totalRead, contentLength)); // contentLength = -1 if unknown
```

Where `contentLength` is read from `response.Content.Headers.ContentLength ?? -1L` before the loop.

**Step 4: Fix all call sites of `DownloadAsync`** (add `null` for progress):
```bash
dotnet build 2>&1 | grep "DownloadAsync"
```

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "ReportsProgress|UnknownContentLength"
```
Expected: 2 tests PASS.

**Step 6: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 7: Commit**
```bash
git add src/FalkForge.Engine/Download/PayloadDownloader.cs tests/FalkForge.Engine.Tests/Download/PayloadDownloaderTests.cs
git commit -m "feat: add progress callback to PayloadDownloader.DownloadAsync"
```

---

## Task 4: `PayloadDownloader` — Resume Support

**Files:**
- Modify: `src/FalkForge.Engine/Download/PayloadDownloader.cs`
- Modify: `tests/FalkForge.Engine.Tests/Download/PayloadDownloaderTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public async Task DownloadAsync_WithPartialFile_ServerSupportsRanges_ResumesFromOffset()
{
    var fullContent = new byte[200_000];
    Random.Shared.NextBytes(fullContent);
    var sha256 = ComputeSha256(fullContent);

    // Pre-write partial file (first 50KB)
    var destPath = Path.GetTempFileName();
    var partialPath = destPath + ".partial";
    await File.WriteAllBytesAsync(partialPath, fullContent[..50_000]);

    var handler = new FakeRangeHttpMessageHandler(fullContent, supportsRanges: true);
    var downloader = new PayloadDownloader(new HttpClient(handler));

    try
    {
        var result = await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, destPath,
            progress: null, allowResume: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(partialPath)); // renamed to destPath
        Assert.Equal(fullContent, await File.ReadAllBytesAsync(destPath));
        Assert.True(handler.RangeRequestReceived); // verify it sent Range header
    }
    finally { File.Delete(destPath); }
}

[Fact]
public async Task DownloadAsync_WithPartialFile_ServerNoRanges_StartsFromScratch()
{
    var fullContent = new byte[100_000];
    Random.Shared.NextBytes(fullContent);
    var sha256 = ComputeSha256(fullContent);

    var destPath = Path.GetTempFileName();
    var partialPath = destPath + ".partial";
    await File.WriteAllBytesAsync(partialPath, new byte[30_000]); // garbage partial

    var handler = new FakeRangeHttpMessageHandler(fullContent, supportsRanges: false);
    var downloader = new PayloadDownloader(new HttpClient(handler));

    try
    {
        var result = await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, destPath,
            progress: null, allowResume: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(partialPath));
    }
    finally { File.Delete(destPath); }
}

[Fact]
public async Task DownloadAsync_OnCancel_AllowResume_KeepsPartialFile()
{
    var content = new byte[500_000];
    var sha256 = ComputeSha256(content);
    var destPath = Path.GetTempFileName();
    var partialPath = destPath + ".partial";
    var cts = new CancellationTokenSource();

    var handler = new SlowHttpMessageHandler(content, cts, cancelAfterBytes: 100_000);
    var downloader = new PayloadDownloader(new HttpClient(handler));

    try
    {
        await downloader.DownloadAsync(
            "https://example.com/update.exe", sha256, destPath,
            progress: null, allowResume: true, cts.Token);

        Assert.True(File.Exists(partialPath));
    }
    finally
    {
        if (File.Exists(partialPath)) File.Delete(partialPath);
        if (File.Exists(destPath)) File.Delete(destPath);
    }
}

[Fact]
public async Task DownloadAsync_OnCancel_AllowResumeFalse_DeletesPartialFile()
{
    var content = new byte[500_000];
    var sha256 = ComputeSha256(content);
    var destPath = Path.GetTempFileName();
    var partialPath = destPath + ".partial";
    var cts = new CancellationTokenSource();

    var handler = new SlowHttpMessageHandler(content, cts, cancelAfterBytes: 100_000);
    var downloader = new PayloadDownloader(new HttpClient(handler));

    await downloader.DownloadAsync(
        "https://example.com/update.exe", sha256, destPath,
        progress: null, allowResume: false, cts.Token);

    Assert.False(File.Exists(partialPath));
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "Partial|AllowResume"
```

**Step 3: Implement resume logic in `PayloadDownloader`**

Key changes:
1. Partial path = `destPath + ".partial"`
2. Add `allowResume` parameter to `DownloadAsync`
3. On start: if `allowResume && File.Exists(partialPath)`:
   - Send `HEAD` to URL, check `response.Headers.Contains("Accept-Ranges") && response.Headers.GetValues("Accept-Ranges").Contains("bytes")`
   - If yes: read `existingSize = new FileInfo(partialPath).Length`, send `GET` with `Range: bytes={existingSize}-`
   - If no: `File.Delete(partialPath)`, proceed with normal GET
4. Write to `.partial` throughout (append mode if resuming, create-new if not)
5. On success: `File.Move(partialPath, destPath, overwrite: true)`
6. SHA-256 verify on `destPath` after move
7. On cancel/failure (catch block): if `!allowResume && File.Exists(partialPath)` → delete

**Step 4: Fix all existing `DownloadAsync` call sites** — add `allowResume: false` (existing callers don't use resume; only `UpdateDownloader` will use `allowResume: true`):
```bash
dotnet build 2>&1 | grep -i "DownloadAsync"
```

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "Partial|AllowResume|ReportsProgress|UnknownContent"
```
Expected: all 6 download tests PASS.

**Step 6: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 7: Commit**
```bash
git add src/FalkForge.Engine/Download/PayloadDownloader.cs tests/FalkForge.Engine.Tests/Download/PayloadDownloaderTests.cs
git commit -m "feat: add resume support to PayloadDownloader with .partial file and Accept-Ranges probe"
```

---

## Task 5: `UpdateDownloader` — New Class

**Files:**
- Create: `src/FalkForge.Engine/Download/UpdateDownloader.cs`
- Create: `tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs`

**Step 1: Write the failing tests**

```csharp
public sealed class UpdateDownloaderTests
{
    [Fact]
    public async Task StartAsync_Success_SendsProgressThenUpdateReadyMessage()
    {
        var sentMessages = new List<EngineMessage>();
        var fakePipe = new FakePipeServer(sentMessages);
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/update.exe"));
        var logger = new NullLogger();

        var downloader = new UpdateDownloader(
            fakeDownloader, fakePipe, logger,
            UpdatePolicy.DownloadAndPrompt, allowResume: true);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", 1_000_000, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        // Must have at least one progress message before UpdateReadyMessage
        var progress = sentMessages.OfType<UpdateDownloadProgressMessage>().ToList();
        var ready = sentMessages.OfType<UpdateReadyMessage>().Single();

        Assert.NotEmpty(progress);
        Assert.Equal("2.0.0", ready.Version);
        Assert.Equal("/cache/update.exe", ready.LocalPath);
        // ready message must come after all progress messages
        Assert.True(sentMessages.IndexOf(ready) > sentMessages.IndexOf(progress.Last()));
    }

    [Fact]
    public async Task StartAsync_DownloadFails_LogsWarning_NoUpdateReadyMessage()
    {
        var sentMessages = new List<EngineMessage>();
        var fakePipe = new FakePipeServer(sentMessages);
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Failure(
            new Error(ErrorKind.DownloadError, "Network timeout")));
        var logger = new CapturingLogger();

        var downloader = new UpdateDownloader(
            fakeDownloader, fakePipe, logger,
            UpdatePolicy.DownloadAndPrompt, allowResume: true);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Empty(sentMessages.OfType<UpdateReadyMessage>());
        Assert.Contains(logger.Warnings, w => w.Contains("Network timeout"));
    }

    [Fact]
    public async Task StartAsync_AutoUpdatePolicy_LaunchesAfterDownload()
    {
        var launched = new List<string>();
        var fakePipe = new FakePipeServer([]);
        var fakeDownloader = new FakePayloadDownloader(Result<string>.Success("/cache/v2.exe"));
        var fakeLauncher = new FakeUpdateLauncher(launched);
        var logger = new NullLogger();

        var downloader = new UpdateDownloader(
            fakeDownloader, fakePipe, logger,
            UpdatePolicy.AutoUpdate, allowResume: false,
            launcher: fakeLauncher);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc123", null, null);
        await downloader.StartAsync(update, "/cache", CancellationToken.None);

        Assert.Single(launched);
        Assert.Equal("/cache/v2.exe", launched[0]);
    }
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "UpdateDownloaderTests"
```

**Step 3: Create `UpdateDownloader.cs`**

```csharp
namespace FalkForge.Engine.Download;

internal sealed class UpdateDownloader(
    PayloadDownloader payloadDownloader,
    PipeServer pipe,
    IEngineLogger logger,
    UpdatePolicy policy,
    bool allowResume,
    IUpdateLauncher? launcher = null)
{
    private readonly IUpdateLauncher _launcher = launcher ?? new DefaultUpdateLauncher();

    internal async Task StartAsync(UpdateInfo update, string cacheDir, CancellationToken ct)
    {
        var destPath = Path.Combine(cacheDir, $"{update.Sha256}.exe");

        var progress = new Progress<(long BytesReceived, long TotalBytes)>(p =>
        {
            int percent = p.TotalBytes > 0
                ? (int)(p.BytesReceived * 100 / p.TotalBytes)
                : 0;
            pipe.Send(new UpdateDownloadProgressMessage
            {
                BytesReceived = p.BytesReceived,
                TotalBytes = p.TotalBytes,
                PercentComplete = percent
            });
        });

        var result = await payloadDownloader.DownloadAsync(
            update.DownloadUrl, update.Sha256, destPath, progress, allowResume, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning($"Update download failed: {result.Error.Message}");
            return;
        }

        pipe.Send(new UpdateReadyMessage
        {
            Version = update.Version,
            LocalPath = result.Value
        });

        if (policy == UpdatePolicy.AutoUpdate)
        {
            var launchResult = _launcher.Launch(result.Value);
            if (!launchResult.IsSuccess)
                logger.LogWarning($"Update launch failed: {launchResult.Error.Message}");
        }
    }
}
```

Note: `IUpdateLauncher` is a thin interface for testability (see Task 6).

**Step 4: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "UpdateDownloaderTests"
```

**Step 5: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 6: Commit**
```bash
git add src/FalkForge.Engine/Download/UpdateDownloader.cs tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs
git commit -m "feat: add UpdateDownloader orchestrating background download with progress and auto-launch"
```

---

## Task 6: `UpdateLauncher` + `IUpdateLauncher`

**Files:**
- Create: `src/FalkForge.Engine/UpdateLauncher.cs`
- Create: `src/FalkForge.Engine/IUpdateLauncher.cs`
- Create: `tests/FalkForge.Engine.Tests/UpdateLauncherTests.cs`

**Step 1: Write the failing tests**

```csharp
public sealed class UpdateLauncherTests
{
    [Fact]
    public void Launch_PathOutsideCacheRoot_ReturnsUPD005()
    {
        var cacheRoot = Path.GetTempPath();
        var launcher = new DefaultUpdateLauncher(cacheRoot);
        var outsidePath = Path.Combine(Path.GetTempPath(), "..", "evil.exe");

        var result = launcher.Launch(outsidePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Launch_NonExistentFile_ReturnsUPD005()
    {
        var cacheRoot = Path.GetTempPath();
        var launcher = new DefaultUpdateLauncher(cacheRoot);
        var missingPath = Path.Combine(cacheRoot, "does-not-exist.exe");

        var result = launcher.Launch(missingPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
    }
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "UpdateLauncherTests"
```

**Step 3: Create `IUpdateLauncher.cs`**
```csharp
namespace FalkForge.Engine;

internal interface IUpdateLauncher
{
    Result<Unit> Launch(string updatePath);
}
```

**Step 4: Create `UpdateLauncher.cs`**
```csharp
namespace FalkForge.Engine;

internal sealed class DefaultUpdateLauncher(string? cacheRoot = null) : IUpdateLauncher
{
    public Result<Unit> Launch(string updatePath)
    {
        // Path containment check
        if (cacheRoot is not null)
        {
            var fullUpdate = Path.GetFullPath(updatePath);
            var fullCache = Path.GetFullPath(cacheRoot);
            if (!fullUpdate.StartsWith(fullCache, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(new Error(ErrorKind.SecurityError,
                    $"UPD005: Update path '{updatePath}' is outside the cache root."));
        }

        if (!File.Exists(updatePath))
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                $"UPD005: Update file not found: '{updatePath}'."));

        try
        {
            Process.Start(new ProcessStartInfo(updatePath) { UseShellExecute = true });
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                $"UPD005: Failed to launch update: {ex.Message}"));
        }
    }
}
```

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "UpdateLauncherTests"
```

**Step 6: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 7: Commit**
```bash
git add src/FalkForge.Engine/ tests/FalkForge.Engine.Tests/UpdateLauncherTests.cs
git commit -m "feat: add UpdateLauncher with path containment check (UPD005)"
```

---

## Task 7: `EngineContext` + `DetectingHandler` — Wire Background Download

**Files:**
- Modify: `src/FalkForge.Engine/EngineContext.cs`
- Modify: `src/FalkForge.Engine/Phases/DetectingHandler.cs`
- Modify: `tests/FalkForge.Engine.Tests/Phases/DetectingHandlerTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public async Task DetectAsync_DownloadAndPromptPolicy_StartsBackgroundDownload()
{
    var context = EngineContextFactory.WithUpdateFeed(
        policy: UpdatePolicy.DownloadAndPrompt,
        updateAvailable: new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc", null, null));
    var handler = new DetectingHandler(/* mocks */);

    await handler.ExecuteAsync(context, CancellationToken.None);

    Assert.NotNull(context.UpdateDownloadTask);
}

[Fact]
public async Task DetectAsync_NotifyOnlyPolicy_DoesNotStartDownloadTask()
{
    var context = EngineContextFactory.WithUpdateFeed(
        policy: UpdatePolicy.NotifyOnly,
        updateAvailable: new UpdateInfo("2.0.0", "https://example.com/v2.exe", "abc", null, null));
    var handler = new DetectingHandler(/* mocks */);

    await handler.ExecuteAsync(context, CancellationToken.None);

    Assert.Null(context.UpdateDownloadTask);
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "DetectAsync.*Policy"
```

**Step 3: Add fields to `EngineContext.cs`**
```csharp
internal Task? UpdateDownloadTask { get; set; }
internal CancellationTokenSource? UpdateDownloadCts { get; set; }
internal string? PendingUpdatePath { get; set; }
```

**Step 4: Update `DetectingHandler.cs`**

After the existing `UpdateAvailableMessage` send (for `NotifyOnly`), add:
```csharp
if (manifest.UpdateFeed?.Policy != UpdatePolicy.NotifyOnly
    && context.AvailableUpdate?.Update is not null)
{
    context.UpdateDownloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var updateDownloader = new UpdateDownloader(
        _payloadDownloader,
        _pipe,
        _logger,
        manifest.UpdateFeed!.Policy,
        manifest.UpdateFeed.AllowResumeDownload);

    context.UpdateDownloadTask = updateDownloader.StartAsync(
        context.AvailableUpdate.Update,
        _cacheLayout.UpdateCacheDir,
        context.UpdateDownloadCts.Token);
}
```

`_payloadDownloader` and `_cacheLayout.UpdateCacheDir` must be injected via `DetectingHandler`'s constructor (add them if not already present).

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "DetectAsync.*Policy"
```

**Step 6: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 7: Commit**
```bash
git add src/FalkForge.Engine/ tests/FalkForge.Engine.Tests/Phases/DetectingHandlerTests.cs
git commit -m "feat: start background UpdateDownloader task in DetectingHandler for non-NotifyOnly policies"
```

---

## Task 8: `EngineHost` — Handle `LaunchUpdateMessage` + Shutdown Cleanup

**Files:**
- Modify: `src/FalkForge.Engine/EngineHost.cs`
- Modify: `tests/FalkForge.Engine.Tests/EngineHostTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public async Task EngineHost_LaunchUpdateMessage_WhenReadyPath_LaunchesAndShutdown()
{
    var launched = new List<string>();
    var host = EngineHostFactory.WithPendingUpdate("/cache/v2.exe", new FakeUpdateLauncher(launched));

    await host.HandleMessageAsync(new LaunchUpdateMessage { SequenceId = 1 });

    Assert.Single(launched);
    Assert.True(host.IsShuttingDown);
}

[Fact]
public async Task EngineHost_LaunchUpdateMessage_NoPendingUpdate_LogsWarningAndIgnores()
{
    var logger = new CapturingLogger();
    var host = EngineHostFactory.WithNoPendingUpdate(logger);

    await host.HandleMessageAsync(new LaunchUpdateMessage { SequenceId = 1 });

    Assert.Contains(logger.Warnings, w => w.Contains("LaunchUpdate"));
    Assert.False(host.IsShuttingDown);
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "EngineHost.*LaunchUpdate"
```

**Step 3: Add `LaunchUpdateMessage` handler in `EngineHost.cs`**

In the message dispatch switch/method:
```csharp
case LaunchUpdateMessage:
    if (_context.PendingUpdatePath is null)
    {
        _logger.LogWarning("LaunchUpdate received but no update is ready — ignoring.");
        break;
    }
    var launchResult = _launcher.Launch(_context.PendingUpdatePath);
    if (!launchResult.IsSuccess)
        _logger.LogWarning($"Update launch failed: {launchResult.Error.Message}");
    _ = _stateMachine.TransitionToAsync(EnginePhase.Shutdown);
    break;
```

**Step 4: Add shutdown cleanup in `EngineHost.cs`**

In the shutdown path:
```csharp
_context.UpdateDownloadCts?.Cancel();
// Await with timeout to allow partial file to flush
if (_context.UpdateDownloadTask is not null)
    await _context.UpdateDownloadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
```

Wrap in try/catch — cancellation exceptions are expected.

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Engine.Tests/ -v --filter "EngineHost.*LaunchUpdate"
```

**Step 6: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 7: Commit**
```bash
git add src/FalkForge.Engine/EngineHost.cs tests/FalkForge.Engine.Tests/EngineHostTests.cs
git commit -m "feat: handle LaunchUpdateMessage in EngineHost, cancel download on shutdown"
```

---

## Task 9: UI Abstractions — `IInstallerEngine` + `InstallerPage` Hooks

**Files:**
- Modify: `src/FalkForge.Ui.Abstractions/IInstallerEngine.cs`
- Modify: `src/FalkForge.Ui/InstallerPage.cs`
- Test: `tests/FalkForge.Ui.Abstractions.Tests/IInstallerEngineTests.cs` (compile test only)

**Step 1: Write the failing test (compilation guard)**

```csharp
[Fact]
public void IInstallerEngine_HasLaunchUpdateMethod()
{
    var method = typeof(IInstallerEngine).GetMethod("LaunchUpdate");
    Assert.NotNull(method);
    Assert.Equal(typeof(void), method!.ReturnType);
    Assert.Empty(method.GetParameters());
}
```

**Step 2: Run test to verify it fails**
```bash
dotnet test tests/FalkForge.Ui.Abstractions.Tests/ -v --filter "HasLaunchUpdateMethod"
```

**Step 3: Add `LaunchUpdate()` to `IInstallerEngine.cs`**
```csharp
/// <summary>
/// Requests the engine to launch the downloaded update and shut down.
/// Only valid after receiving <see cref="UpdateReadyMessage"/> (DownloadAndPrompt policy).
/// </summary>
void LaunchUpdate();
```

**Step 4: Add virtual hooks to `InstallerPage.cs`**

```csharp
/// <summary>Called when an update is detected. Download is starting (DownloadAndPrompt/AutoUpdate).</summary>
protected virtual Task OnUpdateAvailableAsync(string version, string? releaseNotes)
    => Task.CompletedTask;

/// <summary>
/// Called on each download progress tick.
/// When <paramref name="totalBytes"/> is -1, size is unknown — show an indeterminate progress bar.
/// </summary>
protected virtual Task OnUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)
    => Task.CompletedTask;

/// <summary>
/// Called when the update is downloaded and ready to install (DownloadAndPrompt only).
/// Call <see cref="IInstallerEngine.LaunchUpdate"/> when the user confirms.
/// </summary>
protected virtual Task OnUpdateReadyAsync(string version)
    => Task.CompletedTask;
```

Add corresponding internal dispatch methods (called by `CustomShellViewModel`):
```csharp
internal Task DispatchUpdateAvailableAsync(string version, string? releaseNotes)
    => OnUpdateAvailableAsync(version, releaseNotes);
internal Task DispatchUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)
    => OnUpdateProgressAsync(percent, bytesReceived, totalBytes);
internal Task DispatchUpdateReadyAsync(string version)
    => OnUpdateReadyAsync(version);
```

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Ui.Abstractions.Tests/ -v
```

**Step 6: Full build** (will fail — `NullInstallerEngine` doesn't implement `LaunchUpdate` yet; fix in Task 11)
```bash
dotnet build 2>&1 | grep "LaunchUpdate"
```

**Step 7: Commit after Task 11** (combine with NullInstallerEngine, see below)

---

## Task 10: `EngineClient` + `CustomShellViewModel` — Route Update Messages

**Files:**
- Modify: `src/FalkForge.Ui/EngineClient.cs`
- Modify: `src/FalkForge.Ui/ViewModels/CustomShellViewModel.cs`
- Test: `tests/FalkForge.Ui.Tests/EngineClientTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public async Task EngineClient_UpdateDownloadProgressMessage_RaisesProgressEvent()
{
    var events = new List<(int percent, long bytes, long total)>();
    var client = new EngineClient(FakePipe.WithMessages(
        new UpdateDownloadProgressMessage
        {
            BytesReceived = 500_000,
            TotalBytes = 2_000_000,
            PercentComplete = 25
        }));
    client.UpdateDownloadProgress += (p, b, t) => events.Add((p, b, t));

    await client.ProcessNextMessageAsync(CancellationToken.None);

    Assert.Single(events);
    Assert.Equal((25, 500_000L, 2_000_000L), events[0]);
}
```

**Step 2: Run tests to verify they fail**
```bash
dotnet test tests/FalkForge.Ui.Tests/ -v --filter "UpdateDownloadProgress"
```

**Step 3: Add events/callbacks to `EngineClient.cs`**

Add event declarations:
```csharp
public event Action<string, string?>? UpdateAvailable;
public event Action<int, long, long>? UpdateDownloadProgress;  // percent, bytes, total
public event Action<string>? UpdateReady;
```

In the message dispatch switch, add cases:
```csharp
case UpdateDownloadProgressMessage m:
    UpdateDownloadProgress?.Invoke(m.PercentComplete, m.BytesReceived, m.TotalBytes);
    break;
case UpdateAvailableMessage m when m.LocalPath is null: // downloading in progress
    UpdateAvailable?.Invoke(m.Version, m.ReleaseNotes);
    break;
case UpdateReadyMessage m:
    UpdateReady?.Invoke(m.Version);
    break;
```

Implement `LaunchUpdate()` (from `IInstallerEngine`):
```csharp
public void LaunchUpdate() => _pipe.Send(new LaunchUpdateMessage { SequenceId = NextSequenceId() });
```

**Step 4: Update `CustomShellViewModel.cs`**

Subscribe to `EngineClient` events during init and dispatch to active page:
```csharp
_engine.UpdateAvailable += async (v, notes) =>
{
    if (CurrentPage is InstallerPage page)
        await page.DispatchUpdateAvailableAsync(v, notes);
};
_engine.UpdateDownloadProgress += async (pct, bytes, total) =>
{
    if (CurrentPage is InstallerPage page)
        await page.DispatchUpdateProgressAsync(pct, bytes, total);
};
_engine.UpdateReady += async (v) =>
{
    if (CurrentPage is InstallerPage page)
        await page.DispatchUpdateReadyAsync(v);
};
```

**Step 5: Run tests and verify they pass**
```bash
dotnet test tests/FalkForge.Ui.Tests/ -v --filter "UpdateDownloadProgress"
```

**Step 6: Full build and test**
```bash
dotnet build && dotnet test
```

**Step 7: Commit (after Task 11)**

---

## Task 11: `NullInstallerEngine` No-Op + Final Commit

**Files:**
- Modify: `src/FalkForge.Ui/NullInstallerEngine.cs`

**Step 1: Add `LaunchUpdate()` no-op**
```csharp
public void LaunchUpdate() { /* no-op for design-time / test use */ }
```

**Step 2: Full build — zero warnings**
```bash
dotnet build
```

**Step 3: Full test suite**
```bash
dotnet test
```
Expected: all tests pass, zero failures.

**Step 4: Commit Tasks 9 + 10 + 11 together**
```bash
git add src/FalkForge.Ui.Abstractions/ src/FalkForge.Ui/ tests/FalkForge.Ui.Tests/ tests/FalkForge.Ui.Abstractions.Tests/
git commit -m "feat: wire update feed download progress and ready events through UI layer"
```

---

## Task 12: Update Error Codes Documentation

**Files:**
- Modify: `docs/gen/` (whichever source file generates the error codes table in `documentation.html`)

**Step 1: Find the error codes source**
```bash
grep -r "UPD004" docs/gen/ src/
```

**Step 2: Add UPD005**
Add entry for `UPD005: Update launch failed — path outside cache or Process.Start failure`.

**Step 3: Regenerate documentation if applicable**

**Step 4: Commit**
```bash
git add docs/
git commit -m "docs: add UPD005 error code for update launch failure"
```

---

## Final Verification

```bash
dotnet build        # 0 warnings
dotnet test         # all tests pass
```

Confirm the following in a quick manual smoke test:
- `BundleBuilder.UpdateFeed("...", UpdatePolicy.DownloadAndPrompt, allowResume: false)` compiles
- `BundleBuilder.UpdateFeed("...", UpdatePolicy.AutoUpdate)` compiles with `allowResume: true` default
- A custom `InstallerPage` override of `OnUpdateProgressAsync` compiles and wires correctly
