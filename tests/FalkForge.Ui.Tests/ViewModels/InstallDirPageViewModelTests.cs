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

    [Fact]
    public void CanNavigateNext_WhenPathEmpty_ReturnsFalse()
    {
        var shell = MakeShell();
        var vm = GetVm(shell);
        vm.DriveInfoProvider = new FakeDriveInfoProvider(isWritable: true, availableBytes: 500L * 1024 * 1024, longPathsEnabled: true);

        vm.InstallDirectory = string.Empty;

        Assert.False(vm.CanNavigateNext());
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
