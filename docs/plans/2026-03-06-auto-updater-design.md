# Auto-Updater Runtime Design

## Overview

Complete the auto-updater by implementing `DownloadAndPrompt` and `AutoUpdate` policies. The existing infrastructure (UpdateChecker, PayloadDownloader, UpdateDownloader, UpdateLauncher) is 80% complete — this fills the remaining gaps with UI integration, download orchestration, and cache cleanup.

## Architecture

Three policy behaviors, all configurable via `UpdateFeedConfig` on `BundleBuilder`:

| Policy | Download | UI | Launch |
|--------|----------|----|--------|
| **NotifyOnly** | No | `UpdateAvailableMessage` via pipe (existing) | No |
| **DownloadAndPrompt** | Yes, during Detecting | `UpdateAvailablePage` with "Update Now" / "Later" | On user choice |
| **AutoUpdate** | Yes, during Detecting | Subtle progress indicator on welcome page | Configurable: auto-launch or prompt |

**Data flow:**

```
DetectingHandler -> UpdateChecker -> UpdateDownloader (policy switch)
  -> PayloadDownloader (existing, HTTPS+resume+SHA256+retry)
  -> Cache downloaded payload
  -> Send UpdateReady message via pipe
  -> UI shows UpdateAvailablePage or welcome page indicator
  -> User clicks "Update Now" -> UpdateLauncher exits + launches new EXE
```

**Cache cleanup:** On next launch, engine detects it's the newer version and purges older cached payloads.

**Error handling:** Download failures are configurable — silent fallback (default) or non-blocking notification on welcome page. Either way, current session continues normally.

## Model Changes

### UpdateFeedConfig (Compiler.Bundle) — new properties

```csharp
public bool ShowDownloadProgress { get; init; } = true;    // Show indicator on welcome page
public bool ShowDownloadErrors { get; init; }               // Show error notification (default: silent)
public bool PromptBeforeAutoUpdate { get; init; }           // AutoUpdate: prompt vs auto-launch
```

### New Protocol Messages (Engine.Protocol/Messages/)

- `UpdateDownloadProgressMessage` (0x020A) — BytesDownloaded, TotalBytes (nullable), State (Downloading/Complete/Failed)
- `UpdateReadyMessage` (0x020B) — CachedFilePath, Version, Size

### UpdateAvailablePage (Ui/)

- Inherits `InstallerPage<UpdateAvailablePageView>`
- Properties: UpdateVersion, UpdateDescription, UpdateSize, CachedFilePath
- Populated from UpdateReadyMessage received by EngineClient
- "Update Now" calls Engine.LaunchUpdate(CachedFilePath), returns PageResult.Finish
- "Later" returns PageResult.Next (proceeds to Welcome page)
- Respects localization: `!(loc.UpdateAvailable)`, `!(loc.UpdateNow)`, `!(loc.UpdateLater)`

### UpdateProgressIndicator (Ui/Controls/)

- Small UserControl: StackPanel with ProgressBar + TextBlock
- Progress bar (if TotalBytes known) or indeterminate spinner
- Text: "Downloading update..."
- Bound to UpdateDownloadProgress property on WelcomePageVM
- Visibility bound to IsDownloadingUpdate (collapsed when not downloading)

## Engine Changes

### UpdateDownloader (Engine/Download/)

Implement the two stub policies:

```
DownloadAndPrompt:
  1. PayloadDownloader.DownloadAsync() with progress callback
  2. Send UpdateDownloadProgressMessage via pipe during download
  3. Cache payload via PackageCache
  4. Send UpdateReadyMessage with cached path + version
  5. Engine pauses at Detecting, UI shows UpdateAvailablePage
  6. User response (UpdateNow/Later) comes back as message

AutoUpdate:
  1. Same download flow (progress sent for welcome page indicator)
  2. On completion: if PromptBeforeAutoUpdate, same as DownloadAndPrompt
  3. If !PromptBeforeAutoUpdate, UpdateLauncher.Launch() automatically
```

### DetectingHandler (Engine/Phases/)

Extend update check block (lines 105-191):

- Currently: checks feed, sends UpdateAvailableMessage for NotifyOnly
- New: if DownloadAndPrompt or AutoUpdate, call UpdateDownloader.DownloadAsync()
- Pass IProgress<DownloadProgress> that sends UpdateDownloadProgressMessage via pipe
- On failure: log error, if ShowDownloadErrors send error notification, continue detection

### UpdateLauncher — add LaunchAndExit()

- Starts new installer EXE from cached path with original command-line args
- Sends ShutdownMessage to UI
- Engine exits with ExitCodes.UpdateRestart = 4

### Cache Cleanup in DetectingHandler

- After successful version detection, compare BuiltInVariables.BundleVersion with cached payloads
- Delete any cached update payloads with version <= current version

## UI Integration

### UpdateAvailablePage

- `Ui/Views/UpdateAvailablePage.xaml` + `Ui/ViewModels/UpdateAvailablePageVM`
- Inherits InstallerPage<UpdateAvailablePageView>
- "Update Now" calls Engine.LaunchUpdate(CachedFilePath), returns PageResult.Finish
- "Later" returns PageResult.Next

### CustomShellViewModel — page flow

- After Detecting completes, check if UpdateReadyMessage was received
- If yes and policy requires prompt, insert UpdateAvailablePage before Welcome
- If no, normal flow

### EngineClient — new message handlers

- UpdateDownloadProgressMessage: expose as observable/event for UI binding
- UpdateReadyMessage: store cached path + version, raise event for shell VM

### Localization — 4 new keys (en-US + sv-SE)

- UpdateAvailable, UpdateNow, UpdateLater, DownloadingUpdate

## Testing Strategy

**Engine Tests (6 tests):**
1. UpdateDownloader_DownloadAndPrompt_DownloadsAndCaches
2. UpdateDownloader_AutoUpdate_WithPrompt_SendsReadyMessage
3. UpdateDownloader_AutoUpdate_NoPrompt_LaunchesDirectly
4. UpdateDownloader_DownloadFailure_SilentFallback
5. UpdateDownloader_DownloadFailure_ShowsNotification
6. DetectingHandler_CleansUpOlderCachedPayloads

**Protocol Tests (2 tests):**
7. UpdateDownloadProgressMessage_Serialization_Roundtrip
8. UpdateReadyMessage_Serialization_Roundtrip

**UI Tests (3 tests):**
9. UpdateAvailablePageVM_UpdateNow_ReturnsFinish
10. UpdateAvailablePageVM_Later_ReturnsNext
11. WelcomePageVM_DownloadProgress_UpdatesIndicator

**Bundle Compiler Tests (1 test):**
12. UpdateFeedConfig_NewProperties_DefaultValues

12 tests total. No integration tests — existing UpdateChecker/PayloadDownloader integration tests cover real HTTP. New tests mock the downloader to test policy orchestration and UI plumbing.

## Scope

**In scope:**
- DownloadAndPrompt policy: download + UpdateAvailablePage
- AutoUpdate policy: silent download + configurable prompt/auto-launch
- Progress indicator on welcome page during background download
- Configurable error visibility (ShowDownloadErrors)
- Cache cleanup on next successful launch
- Exit-and-launch update mechanism
- 4 localization keys (en-US + sv-SE)

**Not in scope (v1):**
- Delta/differential updates
- Rollback if new version fails to launch
- Update scheduling ("remind me tomorrow")
- Release notes rich content (plain text Description from feed only)
- Multiple simultaneous update feeds
- Metered connection detection
- Digital signature verification of downloaded update (relies on SHA-256 from feed)
