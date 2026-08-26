namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Verifies InstallDirPageViewModel path validation guards:
/// writable probe, free-space check, and path-length limit.
/// </summary>
public class InstallDirPageViewModelTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TestInstallerEngine MakeEngine(string installDir = @"C:\Program Files\TestProduct")
    {
        var engine = new TestInstallerEngine { InstallDirectory = installDir };
        return engine;
    }

    private static DefaultShellViewModel MakeShell(string installDir = @"C:\Program Files\TestProduct")
        => new(MakeEngine(installDir));

    private static InstallDirPageViewModel GetVm(DefaultShellViewModel shell)
        => shell.Pages.OfType<InstallDirPageViewModel>().Single();

    // ── Path length ──────────────────────────────────────────────────────────

    [Fact]
    public void CanNavigateNext_WhenPathExceeds240Chars_ReturnsFalse()
    {
        var longPath = @"C:\" + new string('A', 240);
        var shell = MakeShell(longPath);
        var vm = GetVm(shell);

        vm.DriveInfoProvider = new FakeDriveInfoProvider(
            isWritable: true,
            availableBytes: 500L * 1024 * 1024,  // 500 MB — plenty
            longPathsEnabled: false);
        vm.InstallDirectory = longPath;

        Assert.False(vm.CanNavigateNext());
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("path", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanNavigateNext_WhenPathExceeds240Chars_ButLongPathsEnabled_ReturnsTrue()
    {
        var longPath = @"C:\" + new string('A', 240);
        var shell = MakeShell(longPath);
        var vm = GetVm(shell);

        vm.DriveInfoProvider = new FakeDriveInfoProvider(
            isWritable: true,
            availableBytes: 500L * 1024 * 1024,
            longPathsEnabled: true);
        vm.InstallDirectory = longPath;

        Assert.True(vm.CanNavigateNext());
        Assert.Null(vm.ValidationError);
    }

    // ── Free space ───────────────────────────────────────────────────────────

    [Fact]
    public void CanNavigateNext_WhenInsufficientFreeSpace_ReturnsFalse()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);

        vm.DriveInfoProvider = new FakeDriveInfoProvider(
            isWritable: true,
            availableBytes: 50L * 1024 * 1024,   // only 50 MB — below 100 MB minimum
            longPathsEnabled: true);
        vm.InstallDirectory = @"C:\Program Files\TestProduct";

        Assert.False(vm.CanNavigateNext());
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("space", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanNavigateNext_WhenSufficientFreeSpace_ReturnsTrue()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);

        vm.DriveInfoProvider = new FakeDriveInfoProvider(
            isWritable: true,
            availableBytes: 200L * 1024 * 1024,  // 200 MB — above minimum
            longPathsEnabled: true);
        vm.InstallDirectory = @"C:\Program Files\TestProduct";

        Assert.True(vm.CanNavigateNext());
        Assert.Null(vm.ValidationError);
    }

    // ── Writable probe ───────────────────────────────────────────────────────

    [Fact]
    public void CanNavigateNext_WhenDirectoryNotWritable_ReturnsFalse()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);

        vm.DriveInfoProvider = new FakeDriveInfoProvider(
            isWritable: false,
            availableBytes: 500L * 1024 * 1024,
            longPathsEnabled: true);
        vm.InstallDirectory = @"C:\Program Files\TestProduct";

        Assert.False(vm.CanNavigateNext());
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("writable", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanNavigateNext_WhenDirectoryWritable_ReturnsTrue()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);

        vm.DriveInfoProvider = new FakeDriveInfoProvider(
            isWritable: true,
            availableBytes: 500L * 1024 * 1024,
            longPathsEnabled: true);
        vm.InstallDirectory = @"C:\Program Files\TestProduct";

        Assert.True(vm.CanNavigateNext());
        Assert.Null(vm.ValidationError);
    }

    // ── Basic validation still works ─────────────────────────────────────────

    /// <summary>
    /// A blank box means "do not override anything": every package in the chain installs where
    /// its own MSI puts it. That is the state the page opens in, because a bundle carries no
    /// default directory for the UI to show — the engine's InstallDirectory starts empty and no
    /// manifest field feeds it.
    /// <para>
    /// This assertion was the other way round until 2026-08-26. Measured by driving the real
    /// wizard: the box was empty, Next &gt; was disabled, Back went to a licence page that could
    /// not move forward either, and a fresh install stopped dead on this page. Refusing to
    /// continue is only right for a directory the user typed and got wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void CanNavigateNext_WhenPathEmpty_ReturnsTrue()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);
        vm.DriveInfoProvider = new FakeDriveInfoProvider(isWritable: true, availableBytes: 500L * 1024 * 1024, longPathsEnabled: true);

        vm.InstallDirectory = string.Empty;

        Assert.True(vm.CanNavigateNext());
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void CanNavigateNext_WhenPathIsWhitespace_ReturnsTrue()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);
        vm.DriveInfoProvider = new FakeDriveInfoProvider(isWritable: true, availableBytes: 500L * 1024 * 1024, longPathsEnabled: true);

        vm.InstallDirectory = "   ";

        Assert.True(vm.CanNavigateNext());
    }

    [Fact]
    public void CanNavigateNext_WhenPathIsNotFullyQualified_ReturnsFalse()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);
        vm.DriveInfoProvider = new FakeDriveInfoProvider(isWritable: true, availableBytes: 500L * 1024 * 1024, longPathsEnabled: true);

        vm.InstallDirectory = "not-a-rooted-path";

        Assert.False(vm.CanNavigateNext());
        Assert.NotNull(vm.ValidationError);
    }

    // ── Fake ─────────────────────────────────────────────────────────────────

    private sealed class FakeDriveInfoProvider : IDriveInfoProvider
    {
        private readonly bool _isWritable;
        private readonly long _availableBytes;
        private readonly bool _longPathsEnabled;

        public FakeDriveInfoProvider(bool isWritable, long availableBytes, bool longPathsEnabled)
        {
            _isWritable = isWritable;
            _availableBytes = availableBytes;
            _longPathsEnabled = longPathsEnabled;
        }

        public bool IsWritable(string path) => _isWritable;
        public long GetAvailableFreeSpace(string path) => _availableBytes;
        public bool IsLongPathsEnabled() => _longPathsEnabled;
    }
}
