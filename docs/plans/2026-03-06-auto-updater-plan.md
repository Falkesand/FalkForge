# Auto-Updater Runtime Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete the auto-updater by implementing DownloadAndPrompt and AutoUpdate policies with UI integration, cache cleanup, and configurable error handling.

**Architecture:** The protocol messages (UpdateAvailable, UpdateReady, UpdateDownloadProgress, LaunchUpdate) and EngineClient handlers already exist. InstallerPage already has update lifecycle hooks (OnUpdateAvailableAsync, OnUpdateProgressAsync, OnUpdateReadyAsync). The work is: (1) add config properties, (2) create UpdateAvailablePage, (3) wire page flow in CustomShellViewModel, (4) add welcome page progress indicator, (5) handle DownloadAndPrompt pause/resume in UpdateDownloader, (6) cache cleanup, (7) error handling config.

**Tech Stack:** .NET 10, C# latest, WPF, xUnit, hand-crafted fakes (no Moq)

---

### Task 1: Add Config Properties to UpdateFeedConfig

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/UpdateFeedConfig.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` (UpdateFeed builder section)
- Modify: `src/FalkForge.Engine.Protocol/Manifest/ManifestUpdateFeed.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` (map new fields)
- Test: `tests/FalkForge.Compiler.Bundle.Tests/Builders/UpdateFeedBuilderTests.cs`

**Step 1: Write the failing test**

Add to `tests/FalkForge.Compiler.Bundle.Tests/Builders/UpdateFeedBuilderTests.cs`:

```csharp
[Fact]
public void UpdateFeedConfig_NewProperties_HaveCorrectDefaults()
{
    var config = new UpdateFeedConfig { FeedUrl = "https://example.com/feed.json" };

    Assert.True(config.ShowDownloadProgress);
    Assert.False(config.ShowDownloadErrors);
    Assert.False(config.PromptBeforeAutoUpdate);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter UpdateFeedConfig_NewProperties_HaveCorrectDefaults -v n`
Expected: FAIL — `UpdateFeedConfig` does not have `ShowDownloadProgress` property

**Step 3: Write minimal implementation**

In `src/FalkForge.Compiler.Bundle/UpdateFeedConfig.cs`, add three properties:

```csharp
public sealed class UpdateFeedConfig
{
    public required string FeedUrl { get; init; }
    public UpdatePolicy Policy { get; init; } = UpdatePolicy.NotifyOnly;
    public bool AllowResumeDownload { get; init; } = true;
    public bool ShowDownloadProgress { get; init; } = true;
    public bool ShowDownloadErrors { get; init; }
    public bool PromptBeforeAutoUpdate { get; init; }
}
```

In `src/FalkForge.Engine.Protocol/Manifest/ManifestUpdateFeed.cs`, add matching properties:

```csharp
public bool ShowDownloadProgress { get; init; } = true;
public bool ShowDownloadErrors { get; init; }
public bool PromptBeforeAutoUpdate { get; init; }
```

In `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs`, find where `ManifestUpdateFeed` is created from `UpdateFeedConfig` and add the three new property mappings.

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter UpdateFeedConfig_NewProperties_HaveCorrectDefaults -v n`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Compiler.Bundle/UpdateFeedConfig.cs src/FalkForge.Engine.Protocol/Manifest/ManifestUpdateFeed.cs src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs tests/FalkForge.Compiler.Bundle.Tests/Builders/UpdateFeedBuilderTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(bundle): add ShowDownloadProgress, ShowDownloadErrors, PromptBeforeAutoUpdate config"
```

---

### Task 2: Add Localization Keys

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/Localization/en-US.json`
- Modify: `src/FalkForge.Compiler.Msi/Localization/sv-SE.json`

**Step 1: Add localization keys**

Add to `en-US.json`:
```json
"Dialog.Update.Title": "Update Available",
"Dialog.Update.Description": "A newer version ({0}) is available. Would you like to update now?",
"Button.UpdateNow": "&Update Now",
"Button.UpdateLater": "&Later"
```

Add to `sv-SE.json`:
```json
"Dialog.Update.Title": "Uppdatering tillgänglig",
"Dialog.Update.Description": "En nyare version ({0}) finns tillgänglig. Vill du uppdatera nu?",
"Button.UpdateNow": "&Uppdatera nu",
"Button.UpdateLater": "&Senare"
```

**Step 2: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Compiler.Msi/Localization/en-US.json src/FalkForge.Compiler.Msi/Localization/sv-SE.json
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(localization): add update dialog localization keys for en-US and sv-SE"
```

---

### Task 3: Create UpdateAvailablePage View and ViewModel

**Files:**
- Create: `src/FalkForge.Ui/Views/UpdateAvailablePage.xaml`
- Create: `src/FalkForge.Ui/Views/UpdateAvailablePage.xaml.cs`
- Create: `src/FalkForge.Ui/ViewModels/UpdateAvailablePageViewModel.cs`
- Test: `tests/FalkForge.Ui.Tests/ViewModels/UpdateAvailablePageViewModelTests.cs`

**Context:** InstallerPage<TView> is the base class. TView is a WPF UserControl. The page uses the existing update lifecycle hooks. CustomShellViewModel routes UpdateReadyMessage to the current page. The page's OnNext triggers "Update Now" (launches update), OnBack is "Later" (proceeds to normal flow).

**Step 1: Write the failing test**

Create `tests/FalkForge.Ui.Tests/ViewModels/UpdateAvailablePageViewModelTests.cs`:

```csharp
using FalkForge.Ui.ViewModels;
using Xunit;

namespace FalkForge.Ui.Tests.ViewModels;

public sealed class UpdateAvailablePageViewModelTests
{
    [Fact]
    public void Title_ReturnsUpdateAvailable()
    {
        var vm = new UpdateAvailablePageViewModel();
        Assert.Equal("Update Available", vm.Title);
    }

    [Fact]
    public void OnUpdateReady_SetsVersionAndPath()
    {
        var vm = new UpdateAvailablePageViewModel();
        vm.SetUpdateInfo("2.0.0", "/cache/update.exe", 1024 * 1024);

        Assert.Equal("2.0.0", vm.UpdateVersion);
        Assert.Equal("/cache/update.exe", vm.CachedFilePath);
        Assert.Equal(1024 * 1024, vm.UpdateSize);
    }

    [Fact]
    public void OnNext_WhenUpdateReady_ReturnsFinish()
    {
        var vm = new UpdateAvailablePageViewModel();
        vm.SetUpdateInfo("2.0.0", "/cache/update.exe", 0);

        var result = vm.OnNext();
        Assert.Equal(PageResultKind.Finish, result.Kind);
    }

    [Fact]
    public void OnBack_ReturnsNext_ToSkipToWelcome()
    {
        var vm = new UpdateAvailablePageViewModel();

        var result = vm.OnBack();
        Assert.Equal(PageResultKind.Next, result.Kind);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj --filter UpdateAvailablePageViewModelTests -v n`
Expected: FAIL — class not found

**Step 3: Write minimal implementation**

Create `src/FalkForge.Ui/ViewModels/UpdateAvailablePageViewModel.cs`:

```csharp
using FalkForge.Ui.Abstractions;

namespace FalkForge.Ui.ViewModels;

public sealed class UpdateAvailablePageViewModel : InstallerPage<Views.UpdateAvailablePage>
{
    private string? _updateVersion;
    private string? _cachedFilePath;
    private long _updateSize;

    public override string Title => "Update Available";

    public string? UpdateVersion
    {
        get => _updateVersion;
        private set { _updateVersion = value; OnPropertyChanged(); }
    }

    public string? CachedFilePath
    {
        get => _cachedFilePath;
        private set { _cachedFilePath = value; OnPropertyChanged(); }
    }

    public long UpdateSize
    {
        get => _updateSize;
        private set { _updateSize = value; OnPropertyChanged(); }
    }

    public void SetUpdateInfo(string version, string cachedPath, long size)
    {
        UpdateVersion = version;
        CachedFilePath = cachedPath;
        UpdateSize = size;
    }

    public override PageResult OnNext()
    {
        if (CachedFilePath is not null)
            Engine.LaunchUpdate();
        return PageResult.Finish;
    }

    public override PageResult OnBack() => PageResult.Next;

    public override bool CanGoBack => true;
}
```

Create `src/FalkForge.Ui/Views/UpdateAvailablePage.xaml`:

```xml
<UserControl x:Class="FalkForge.Ui.Views.UpdateAvailablePage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="164" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <Grid Grid.Column="0" Background="{DynamicResource WatermarkBrush}">
            <Image Source="{DynamicResource WatermarkImage}" Stretch="Fill" />
        </Grid>

        <StackPanel Grid.Column="1" Margin="20,16,16,16">
            <TextBlock Text="{Binding Title}" Style="{StaticResource ExteriorTitle}" />
            <TextBlock Style="{StaticResource ExteriorDescription}">
                <Run Text="A newer version (" /><Run Text="{Binding UpdateVersion, Mode=OneWay}" FontWeight="Bold" /><Run Text=") is available." />
            </TextBlock>

            <StackPanel Margin="0,24,0,0" Orientation="Horizontal">
                <Button Content="_Update Now" Margin="0,0,8,0"
                        Command="{Binding NextCommand}" IsDefault="True"
                        Style="{StaticResource PrimaryButton}" />
                <Button Content="_Later"
                        Command="{Binding BackCommand}"
                        Style="{StaticResource SecondaryButton}" />
            </StackPanel>
        </StackPanel>
    </Grid>
</UserControl>
```

Create `src/FalkForge.Ui/Views/UpdateAvailablePage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace FalkForge.Ui.Views;

public partial class UpdateAvailablePage : UserControl
{
    public UpdateAvailablePage()
    {
        InitializeComponent();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj --filter UpdateAvailablePageViewModelTests -v n`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Ui/Views/UpdateAvailablePage.xaml src/FalkForge.Ui/Views/UpdateAvailablePage.xaml.cs src/FalkForge.Ui/ViewModels/UpdateAvailablePageViewModel.cs tests/FalkForge.Ui.Tests/ViewModels/UpdateAvailablePageViewModelTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(ui): add UpdateAvailablePage with Update Now / Later buttons"
```

---

### Task 4: Wire UpdateAvailablePage into CustomShellViewModel Page Flow

**Files:**
- Modify: `src/FalkForge.Ui/ViewModels/CustomShellViewModel.cs`

**Context:** CustomShellViewModel already routes UpdateReady events to the current page. The new behavior: when UpdateReadyMessage is received AND policy requires prompt (DownloadAndPrompt, or AutoUpdate with PromptBeforeAutoUpdate), insert UpdateAvailablePage at index 0 and navigate to it. The update feed config needs to flow from the manifest through to the shell VM.

**Step 1: Write the failing test**

Add to `tests/FalkForge.Ui.Tests/ViewModels/` a new test file `CustomShellViewModelUpdateTests.cs`:

```csharp
using FalkForge.Ui.ViewModels;
using Xunit;

namespace FalkForge.Ui.Tests.ViewModels;

public sealed class CustomShellViewModelUpdateTests
{
    [StaFact]
    public async Task UpdateReady_WithPromptPolicy_InsertsUpdatePage()
    {
        // Arrange: Create shell VM with mock engine that has DownloadAndPrompt policy
        // Act: Simulate UpdateReady event
        // Assert: CurrentPage is UpdateAvailablePageViewModel
    }

    [StaFact]
    public async Task UpdateReady_WithNotifyOnly_DoesNotInsertPage()
    {
        // Arrange: NotifyOnly policy
        // Act: Simulate UpdateReady event
        // Assert: CurrentPage unchanged (still Welcome)
    }
}
```

Note: The exact test implementation depends on the existing test infrastructure for CustomShellViewModel. The subagent should read the existing tests in `tests/FalkForge.Ui.Tests/ViewModels/` to understand the mocking pattern, then adapt. Key behavior to test: when UpdateReady fires and policy is DownloadAndPrompt, an UpdateAvailablePage is inserted and navigated to.

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj --filter CustomShellViewModelUpdateTests -v n`
Expected: FAIL

**Step 3: Write minimal implementation**

In `CustomShellViewModel.cs`, modify the `UpdateReady` event handler (currently just dispatches to current page). New behavior:

```csharp
engineClient.UpdateReady += async version =>
{
    // Check if policy requires prompt
    var feed = engine.Manifest?.UpdateFeed;
    bool shouldPrompt = feed?.Policy == UpdatePolicy.DownloadAndPrompt
        || (feed?.Policy == UpdatePolicy.AutoUpdate && feed.PromptBeforeAutoUpdate);

    if (shouldPrompt)
    {
        // Create and insert UpdateAvailablePage at current position
        var updatePage = new UpdateAvailablePageViewModel();
        updatePage.SetUpdateInfo(version, context.PendingUpdatePath, 0);
        // Wire engine/state
        updatePage.Engine = engine;
        updatePage.SharedState = _sharedState;

        _pages.Insert(_currentPageIndex + 1, updatePage);
        _currentPageIndex++;
        await ActivateCurrentPageAsync();
    }
    else if (CurrentPage is InstallerPage page)
    {
        await page.DispatchUpdateReadyAsync(version);
    }
};
```

The exact integration depends on how `EngineContext.PendingUpdatePath` is exposed to the UI. The subagent should read how the manifest and context flow to understand the right property access. If `PendingUpdatePath` isn't accessible from the UI side, the `UpdateReadyMessage` may need to carry the `LocalPath` (check its fields — research shows it has `Version` and `LocalPath`).

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj --filter CustomShellViewModelUpdateTests -v n`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Ui/ViewModels/CustomShellViewModel.cs tests/FalkForge.Ui.Tests/ViewModels/CustomShellViewModelUpdateTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(ui): wire UpdateAvailablePage into shell VM page flow on UpdateReady"
```

---

### Task 5: Add Welcome Page Progress Indicator

**Files:**
- Modify: `src/FalkForge.Ui/Views/WelcomePage.xaml`
- Modify: `src/FalkForge.Ui/ViewModels/WelcomePageViewModel.cs`
- Test: `tests/FalkForge.Ui.Tests/ViewModels/WelcomePageViewModelUpdateTests.cs`

**Context:** WelcomePageViewModel inherits InstallerPage which has `OnUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)` virtual hook. Override it to expose progress properties. Add a small StackPanel to the XAML that shows a progress bar + "Downloading update..." text, collapsed when not downloading.

**Step 1: Write the failing test**

Create `tests/FalkForge.Ui.Tests/ViewModels/WelcomePageViewModelUpdateTests.cs`:

```csharp
using FalkForge.Ui.ViewModels;
using Xunit;

namespace FalkForge.Ui.Tests.ViewModels;

public sealed class WelcomePageViewModelUpdateTests
{
    [Fact]
    public async Task OnUpdateProgress_SetsProgressProperties()
    {
        var vm = new WelcomePageViewModel();

        await vm.DispatchUpdateProgressAsync(42, 420_000, 1_000_000);

        Assert.Equal(42, vm.DownloadPercent);
        Assert.True(vm.IsDownloadingUpdate);
    }

    [Fact]
    public void IsDownloadingUpdate_DefaultsFalse()
    {
        var vm = new WelcomePageViewModel();
        Assert.False(vm.IsDownloadingUpdate);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj --filter WelcomePageViewModelUpdateTests -v n`
Expected: FAIL — `DownloadPercent` property not found

**Step 3: Write minimal implementation**

Add to `WelcomePageViewModel.cs`:

```csharp
private int _downloadPercent;
private bool _isDownloadingUpdate;

public int DownloadPercent
{
    get => _downloadPercent;
    private set { _downloadPercent = value; OnPropertyChanged(); }
}

public bool IsDownloadingUpdate
{
    get => _isDownloadingUpdate;
    private set { _isDownloadingUpdate = value; OnPropertyChanged(); }
}

protected override Task OnUpdateProgressAsync(int percent, long bytesReceived, long totalBytes)
{
    DownloadPercent = percent;
    IsDownloadingUpdate = true;
    return Task.CompletedTask;
}
```

Add to `WelcomePage.xaml`, at the bottom of the content StackPanel (Grid.Column="1"):

```xml
<StackPanel Orientation="Horizontal" Margin="0,16,0,0"
            Visibility="{Binding IsDownloadingUpdate, Converter={StaticResource BoolToVisibility}}">
    <ProgressBar Width="120" Height="16" Minimum="0" Maximum="100"
                 Value="{Binding DownloadPercent, Mode=OneWay}" />
    <TextBlock Text="Downloading update..." Margin="8,0,0,0"
               VerticalAlignment="Center" FontSize="11"
               Foreground="{DynamicResource SubtleTextBrush}" />
</StackPanel>
```

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj --filter WelcomePageViewModelUpdateTests -v n`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Ui/Views/WelcomePage.xaml src/FalkForge.Ui/ViewModels/WelcomePageViewModel.cs tests/FalkForge.Ui.Tests/ViewModels/WelcomePageViewModelUpdateTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(ui): add download progress indicator to welcome page"
```

---

### Task 6: Implement DownloadAndPrompt in UpdateDownloader

**Files:**
- Modify: `src/FalkForge.Engine/Download/UpdateDownloader.cs`
- Test: `tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs`

**Context:** UpdateDownloader (98 lines) currently: downloads → sends progress → sends UpdateReadyMessage → if AutoUpdate, calls launcher. For DownloadAndPrompt: after sending UpdateReadyMessage, the downloader's job is done — the UI side handles the prompt (Task 4). For AutoUpdate with PromptBeforeAutoUpdate: same as DownloadAndPrompt (don't call launcher). The PromptBeforeAutoUpdate flag needs to be passed through.

**Step 1: Write the failing test**

Add to `tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs`:

```csharp
[Fact]
public async Task StartAsync_AutoUpdateWithPrompt_DoesNotLaunch()
{
    // Arrange: Create downloader with AutoUpdate policy AND promptBeforeAutoUpdate=true
    // Fake launcher that tracks if Launch was called
    var launched = false;
    var launcher = new FakeLauncher(() => launched = true);
    var downloader = CreateDownloader(
        policy: UpdatePolicy.AutoUpdate,
        promptBeforeAutoUpdate: true,
        launcher: launcher);

    // Act
    await downloader.StartAsync(TestUpdate, "/cache", CancellationToken.None);

    // Assert: Launcher should NOT have been called
    Assert.False(launched);
}

[Fact]
public async Task StartAsync_AutoUpdateWithoutPrompt_Launches()
{
    var launched = false;
    var launcher = new FakeLauncher(() => launched = true);
    var downloader = CreateDownloader(
        policy: UpdatePolicy.AutoUpdate,
        promptBeforeAutoUpdate: false,
        launcher: launcher);

    await downloader.StartAsync(TestUpdate, "/cache", CancellationToken.None);

    Assert.True(launched);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "StartAsync_AutoUpdateWithPrompt_DoesNotLaunch|StartAsync_AutoUpdateWithoutPrompt_Launches" -v n`
Expected: FAIL — `promptBeforeAutoUpdate` parameter not recognized

**Step 3: Write minimal implementation**

Modify `UpdateDownloader` constructor to accept `bool promptBeforeAutoUpdate`:

```csharp
internal UpdateDownloader(
    Func<string, string, string, IProgress<(long, long)>?, bool, CancellationToken, Task<Result<string>>> download,
    Func<EngineMessage, CancellationToken, Task> sendMessage,
    IEngineLogger logger,
    UpdatePolicy policy,
    bool allowResume,
    bool promptBeforeAutoUpdate = false,
    IUpdateLauncher? launcher = null)
```

Store as `_promptBeforeAutoUpdate`. Modify the launch decision (around line 75):

```csharp
if (_policy == UpdatePolicy.AutoUpdate && !_promptBeforeAutoUpdate && _launcher is not null)
{
    _launcher.Launch(downloadResult.Value);
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "StartAsync_AutoUpdateWithPrompt_DoesNotLaunch|StartAsync_AutoUpdateWithoutPrompt_Launches" -v n`
Expected: PASS

**Step 5: Update DetectingHandler to pass promptBeforeAutoUpdate**

In `DetectingHandler.cs`, where `UpdateDownloader` is instantiated (around line 180), pass the new flag from `context.Manifest.UpdateFeed.PromptBeforeAutoUpdate`.

**Step 6: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Engine/Download/UpdateDownloader.cs src/FalkForge.Engine/Phases/DetectingHandler.cs tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(engine): respect PromptBeforeAutoUpdate flag in UpdateDownloader"
```

---

### Task 7: Add Download Error Handling with ShowDownloadErrors Config

**Files:**
- Modify: `src/FalkForge.Engine/Download/UpdateDownloader.cs`
- Modify: `src/FalkForge.Engine/Phases/DetectingHandler.cs`
- Test: `tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs`

**Context:** When download fails, current behavior logs a warning. New behavior: if `ShowDownloadErrors` is true, send an error notification message to UI. The existing `ErrorMessage` (type 0x0108) can carry this. Detection continues either way.

**Step 1: Write the failing test**

Add to `tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs`:

```csharp
[Fact]
public async Task StartAsync_DownloadFails_ShowErrors_SendsErrorMessage()
{
    var messages = new List<EngineMessage>();
    var downloader = CreateDownloader(
        downloadResult: Result<string>.Failure(ErrorKind.DownloadError, "Network error"),
        showDownloadErrors: true,
        messageSink: messages);

    await downloader.StartAsync(TestUpdate, "/cache", CancellationToken.None);

    Assert.Contains(messages, m => m is ErrorMessage);
}

[Fact]
public async Task StartAsync_DownloadFails_SilentFallback_NoErrorMessage()
{
    var messages = new List<EngineMessage>();
    var downloader = CreateDownloader(
        downloadResult: Result<string>.Failure(ErrorKind.DownloadError, "Network error"),
        showDownloadErrors: false,
        messageSink: messages);

    await downloader.StartAsync(TestUpdate, "/cache", CancellationToken.None);

    Assert.DoesNotContain(messages, m => m is ErrorMessage);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "StartAsync_DownloadFails_ShowErrors|StartAsync_DownloadFails_SilentFallback" -v n`
Expected: FAIL — `showDownloadErrors` parameter not recognized

**Step 3: Write minimal implementation**

Add `bool showDownloadErrors` parameter to `UpdateDownloader` constructor. In the download failure branch (where warning is logged), add:

```csharp
if (_showDownloadErrors)
{
    await _sendMessage(new ErrorMessage
    {
        ErrorCode = ErrorKind.DownloadError,
        Message = $"Update download failed: {downloadResult.Error.Message}"
    }, ct);
}
```

Also update `DetectingHandler` to pass `context.Manifest.UpdateFeed.ShowDownloadErrors` through.

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "StartAsync_DownloadFails_ShowErrors|StartAsync_DownloadFails_SilentFallback" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Engine/Download/UpdateDownloader.cs src/FalkForge.Engine/Phases/DetectingHandler.cs tests/FalkForge.Engine.Tests/Download/UpdateDownloaderTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(engine): configurable download error notification via ShowDownloadErrors"
```

---

### Task 8: Add Cache Cleanup for Old Update Payloads

**Files:**
- Modify: `src/FalkForge.Engine/Phases/DetectingHandler.cs`
- Test: `tests/FalkForge.Engine.Tests/Phases/DetectingHandlerTests.cs`

**Context:** After successful detection, compare current BundleVersion with cached update payloads in `%LOCALAPPDATA%/FalkForge/UpdateCache/{BundleId}/`. Delete any files where the filename contains a version <= current version. The cache directory structure uses version in the filename (set by UpdateDownloader when caching).

**Step 1: Write the failing test**

Add to `tests/FalkForge.Engine.Tests/Phases/DetectingHandlerTests.cs`:

```csharp
[Fact]
public async Task ExecuteAsync_CleansUpOlderCachedPayloads()
{
    // Arrange: Create temp cache dir with files for version 1.0.0 (older) and 2.0.0 (current)
    var cacheDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(cacheDir);
    var oldFile = Path.Combine(cacheDir, "update-1.0.0.exe");
    var currentFile = Path.Combine(cacheDir, "update-2.0.0.exe");
    File.WriteAllText(oldFile, "old");
    File.WriteAllText(currentFile, "current");

    try
    {
        // Create handler with BundleVersion = 2.0.0 and cache dir pointing here
        var context = CreateContext(bundleVersion: "2.0.0", updateCacheDir: cacheDir);
        var handler = new DetectingHandler(new PackageDetector(/* ... */));

        await handler.ExecuteAsync(context, CancellationToken.None);

        // Assert: old file deleted, current file kept
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(currentFile));
    }
    finally
    {
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
    }
}
```

Note: The exact test setup depends on how `DetectingHandler` accesses the cache directory. The subagent should read the existing `DetectingHandlerTests.cs` to understand the context creation pattern, and adapt. If the cache dir path is derived from `EngineContext` properties, mock those.

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter CleansUpOlderCachedPayloads -v n`
Expected: FAIL — no cleanup logic exists

**Step 3: Write minimal implementation**

Add a private method to `DetectingHandler`:

```csharp
private static void CleanupOlderUpdates(string cacheDir, string currentVersion)
{
    if (!Directory.Exists(cacheDir)) return;

    var current = Version.TryParse(currentVersion, out var v) ? v : null;
    if (current is null) return;

    foreach (var file in Directory.GetFiles(cacheDir))
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        // Extract version from filename pattern "update-X.Y.Z"
        var versionPart = fileName.Replace("update-", "");
        if (Version.TryParse(versionPart, out var fileVersion) && fileVersion <= current)
        {
            try { File.Delete(file); }
            catch { /* best effort */ }
        }
    }
}
```

Call it at the end of `ExecuteAsync`, after detection completes:

```csharp
var cacheDir = GetUpdateCacheDir(context);
if (cacheDir is not null)
    CleanupOlderUpdates(cacheDir, context.Variables.Get(BuiltInVariables.BundleVersion));
```

**Step 4: Run test to verify it passes**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter CleansUpOlderCachedPayloads -v n`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add src/FalkForge.Engine/Phases/DetectingHandler.cs tests/FalkForge.Engine.Tests/Phases/DetectingHandlerTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "feat(engine): clean up older cached update payloads on detection"
```

---

### Task 9: Protocol Serialization Tests for New Config Fields

**Files:**
- Modify: `tests/FalkForge.Engine.Protocol.Tests/UpdateMessageTests.cs`

**Context:** The ManifestUpdateFeed now has 3 new bool properties. Verify they survive manifest serialization/deserialization roundtrip. Also verify the existing UpdateDownloadProgress and UpdateReady messages still roundtrip correctly (regression safety).

**Step 1: Write the failing test**

Add to `tests/FalkForge.Engine.Protocol.Tests/UpdateMessageTests.cs`:

```csharp
[Fact]
public void ManifestUpdateFeed_NewProperties_Roundtrip()
{
    var feed = new ManifestUpdateFeed
    {
        FeedUrl = "https://example.com/feed.json",
        Policy = UpdatePolicy.AutoUpdate,
        AllowResumeDownload = true,
        ShowDownloadProgress = false,
        ShowDownloadErrors = true,
        PromptBeforeAutoUpdate = true
    };

    // Serialize via ManifestJsonContext and deserialize
    var json = JsonSerializer.Serialize(feed, ManifestJsonContext.Default.ManifestUpdateFeed);
    var deserialized = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.ManifestUpdateFeed);

    Assert.NotNull(deserialized);
    Assert.False(deserialized.ShowDownloadProgress);
    Assert.True(deserialized.ShowDownloadErrors);
    Assert.True(deserialized.PromptBeforeAutoUpdate);
}
```

Note: Verify ManifestJsonContext includes ManifestUpdateFeed. If it uses source generation, the new properties may need to be added to the context.

**Step 2: Run test to verify it fails**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Protocol.Tests/FalkForge.Engine.Protocol.Tests.csproj --filter ManifestUpdateFeed_NewProperties_Roundtrip -v n`
Expected: Could PASS or FAIL depending on source-gen setup. If fail, update ManifestJsonContext.

**Step 3: Fix if needed**

If source-generated JSON context doesn't pick up new properties automatically, add them to the context attribute.

**Step 4: Run all protocol tests**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/tests/FalkForge.Engine.Protocol.Tests/FalkForge.Engine.Protocol.Tests.csproj -v n`
Expected: ALL PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add tests/FalkForge.Engine.Protocol.Tests/UpdateMessageTests.cs
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "test(protocol): verify ManifestUpdateFeed new properties survive roundtrip"
```

---

### Task 10: Run Full Test Suite and Fix Any Issues

**Files:** Any files that need fixes

**Step 1: Build entire solution**

Run: `dotnet build D:/Git/FalkInstaller/.worktrees/auto-updater/FalkForge.slnx`
Expected: 0 warnings, 0 errors (except known pre-existing Engine CS8604/CS0219)

**Step 2: Run all tests**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/auto-updater/FalkForge.slnx --verbosity normal`
Expected: All ~2484+ tests pass

**Step 3: Fix any failures**

If tests fail, investigate and fix. Common issues:
- Constructor signature changes breaking existing tests (add default parameter values)
- Missing using directives
- WPF tests needing `[StaFact]` instead of `[Fact]`

**Step 4: Commit any fixes**

```bash
git -C D:/Git/FalkInstaller/.worktrees/auto-updater add -A
git -C D:/Git/FalkInstaller/.worktrees/auto-updater commit -m "fix: resolve test failures from auto-updater integration"
```
