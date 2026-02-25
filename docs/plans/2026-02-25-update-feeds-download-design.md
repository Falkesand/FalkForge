# Update Feeds: Background Download & Launch

**Date:** 2026-02-25
**Status:** Design
**Scope:** Complete `DownloadAndPrompt` and `AutoUpdate` policies — background download with progress reporting, resume support, and update launch

---

## Current State

The update feed pipeline is partially implemented:

- ✅ Feed fetch, JSON parse, version comparison (`UpdateChecker`, `UpdateFeedParser`)
- ✅ `NotifyOnly` policy: detect + send `UpdateAvailableMessage` to UI
- ✅ `UpdateReadyMessage` protocol message exists but is never sent
- ❌ `DownloadAndPrompt` and `AutoUpdate` both fall back to `NotifyOnly` (noted in code comment)
- ❌ No download progress reporting
- ❌ No launch/restart mechanism

---

## Design

### 1. Protocol Layer (`Engine.Protocol`)

Two new messages:

**`UpdateDownloadProgressMessage` (Engine → UI, `0x010E`)**
```csharp
public sealed class UpdateDownloadProgressMessage : EngineMessage
{
    public override MessageType Type => MessageType.UpdateDownloadProgress;
    public required long BytesReceived { get; init; }
    public required long TotalBytes { get; init; }      // -1 if server did not provide Content-Length
    public required int PercentComplete { get; init; }  // 0-100; 0 when TotalBytes == -1
}
```

**`LaunchUpdateMessage` (UI → Engine, `0x010F`)**
```csharp
public sealed class LaunchUpdateMessage : EngineMessage
{
    public override MessageType Type => MessageType.LaunchUpdate;
    // No payload — intent signal only
}
```

`UpdateReadyMessage` (already exists, `0x010D`) — no changes needed.

---

### 2. `UpdateFeedConfig` + `ManifestUpdateFeed` — Resume Support

`UpdateFeedConfig` gains one new field:

```csharp
public sealed class UpdateFeedConfig
{
    public required string FeedUrl { get; init; }
    public UpdatePolicy Policy { get; init; } = UpdatePolicy.NotifyOnly;
    public bool AllowResumeDownload { get; init; } = true;
}
```

`ManifestUpdateFeed` gains the same field so the compiler-to-runtime mapping carries it through:

```csharp
public sealed record ManifestUpdateFeed(string FeedUrl, UpdatePolicy Policy, bool AllowResumeDownload);
```

`ManifestGenerator` maps the new field. `BundleBuilder.UpdateFeed()` accepts the new parameter.

---

### 3. `PayloadDownloader` Changes

Add `IProgress<(long BytesReceived, long TotalBytes)>?` parameter to `DownloadAsync`. Progress fires after each 81KB chunk in the existing streaming loop.

**Resume logic:**
- Partial file stored as `<sha256>.partial` in the cache directory
- On start, if `.partial` exists **and** `allowResume = true`:
  - Probe server with `HEAD` for `Accept-Ranges: bytes` header
  - If supported: `GET` with `Range: bytes=<partialSize>-`, open `.partial` for append
  - If not supported: delete `.partial`, start fresh
- Progress `BytesReceived` starts from the partial file's existing size so the bar continues from where it left off
- On cancel/failure:
  - `allowResume = true` → keep `.partial`
  - `allowResume = false` → delete `.partial`
- SHA-256 verified on the complete assembled file only (after rename from `.partial`)

---

### 4. `UpdateDownloader` (new, `Engine/Download/`)

Owns the background download lifecycle:

```csharp
internal sealed class UpdateDownloader(
    PayloadDownloader payloadDownloader,
    PipeServer pipe,
    IEngineLogger logger,
    UpdatePolicy policy,
    bool allowResume)
{
    internal Task StartAsync(UpdateInfo update, string cacheDir, CancellationToken ct);
}
```

**`StartAsync` flow:**
1. Build `.partial` path from SHA-256
2. Create progress callback → sends `UpdateDownloadProgressMessage` per chunk
3. Call `PayloadDownloader.DownloadAsync(url, sha256, destPath, progress, ct)`
4. On success: send `UpdateReadyMessage(version, finalPath)`
5. For `AutoUpdate`: call `UpdateLauncher.Launch(finalPath)` → engine shuts down
6. On failure: log warning only — update failure never blocks the install

---

### 5. `UpdateLauncher` (new, `Engine/`)

```csharp
internal static class UpdateLauncher
{
    internal static Result<Unit> Launch(string updatePath)
    // Path containment check against cache root
    // Process.Start(updatePath, UseShellExecute = true) — lets OS handle UAC if needed
    // Returns Result<Unit>
}
```

`UseShellExecute = true` allows Windows to handle UAC elevation for the new installer without the engine needing to know the target's privilege requirements.

---

### 6. `EngineContext` Changes

```csharp
internal Task? UpdateDownloadTask { get; set; }
internal CancellationTokenSource? UpdateDownloadCts { get; set; }
internal string? PendingUpdatePath { get; set; }
```

`UpdateDownloadTask` is started as a fire-and-forget background task at the end of `DetectingHandler`, for `DownloadAndPrompt` and `AutoUpdate` policies only.

---

### 7. `DetectingHandler` Changes

After sending `UpdateAvailableMessage`, for non-`NotifyOnly` policies:

```csharp
context.UpdateDownloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
context.UpdateDownloadTask = new UpdateDownloader(...).StartAsync(
    update, cacheDir, manifest.UpdateFeed.AllowResumeDownload,
    context.UpdateDownloadCts.Token);
```

---

### 8. `EngineHost` Changes

**Handle `LaunchUpdateMessage`:**
- Validate `EngineContext.PendingUpdatePath != null` — if not set, log warning and ignore
- Call `UpdateLauncher.Launch(PendingUpdatePath)`
- Initiate shutdown sequence

**Handle shutdown/cancel:**
- Cancel `UpdateDownloadCts` before shutdown
- Partial file disposition follows `AllowResumeDownload` policy

---

### 9. UI Layer

**`IInstallerEngine` — new method:**
```csharp
void LaunchUpdate();
```

**`InstallerPage` — three new virtual lifecycle hooks:**
```csharp
protected virtual Task OnUpdateAvailableAsync(string version, string? releaseNotes)
    => Task.CompletedTask;

protected virtual Task OnUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)
    => Task.CompletedTask;

// DownloadAndPrompt only — AutoUpdate never reaches this hook, it auto-launches
protected virtual Task OnUpdateReadyAsync(string version)
    => Task.CompletedTask;
```

When `totalBytes == -1` → UI sets `ProgressBar.IsIndeterminate = true` (WPF Knight Rider animation). When size is known → normal percentage fill.

**`EngineClient`** receives pipe messages and raises events for each update message type.

**`CustomShellViewModel`** subscribes and dispatches to the **currently active page** via the hooks above.

**`NullInstallerEngine`** gets a no-op `LaunchUpdate()` for design-time/test use.

---

## Error Codes

| Code | Description |
|------|-------------|
| UPD001 | (existing) HTTP/network error, timeout, size limit — also covers resume probe failure |
| UPD005 | Update launch failed — path validation or `Process.Start` failure |

---

## Testing Strategy

- **`UpdateDownloaderTests`**: mock `PayloadDownloader` + `PipeServer`; verify messages sent in order (progress ticks → `UpdateReadyMessage`)
- **`PayloadDownloaderTests`**: extend with progress callback tests; resume tests using mock HTTP handler
- **`UpdateLauncherTests`**: path outside cache rejected (UPD005); valid path succeeds
- **`EngineHostTests`**: `LaunchUpdateMessage` accepted when `PendingUpdatePath` is set; silently ignored when not set
- **`ManifestGeneratorTests`**: `AllowResumeDownload` mapped correctly for both true and false
