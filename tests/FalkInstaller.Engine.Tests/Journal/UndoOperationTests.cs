namespace FalkInstaller.Engine.Tests.Journal;

using FalkInstaller.Engine.Journal;
using FalkInstaller.Engine.Journal.UndoOperations;
using FalkInstaller.Engine.Tests.Mocks;
using Xunit;

public sealed class UndoOperationTests
{
    // --- MsiUninstallOperation Tests ---

    [Fact]
    public void MsiUninstall_CanHandle_MsiInstalled_ReturnsTrue()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new MsiUninstallOperation(runner);

        var entry = CreateEntry(JournalEntryType.MsiInstalled);

        Assert.True(op.CanHandle(entry));
    }

    [Fact]
    public void MsiUninstall_CanHandle_ExeInstalled_ReturnsFalse()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new MsiUninstallOperation(runner);

        var entry = CreateEntry(JournalEntryType.ExeInstalled);

        Assert.False(op.CanHandle(entry));
    }

    [Fact]
    public async Task MsiUninstall_ValidProductCode_CallsMsiexecWithCorrectArgs()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled,
            productCode: "{12345678-1234-1234-1234-123456789ABC}");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("msiexec.exe", runner.LastFileName);
        Assert.Equal("/x {12345678-1234-1234-1234-123456789ABC} /qn /norestart", runner.LastArguments);
    }

    [Fact]
    public async Task MsiUninstall_ExitCode1605_ProductNotInstalled_ReturnsSuccess()
    {
        var runner = new MockProcessRunner().WithExitCode(1605);
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled,
            productCode: "{12345678-1234-1234-1234-123456789ABC}");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task MsiUninstall_NonZeroExitCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(1603);
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled,
            productCode: "{12345678-1234-1234-1234-123456789ABC}");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }

    [Fact]
    public async Task MsiUninstall_MissingProductCode_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled, productCode: null);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("ProductCode", result.Error.Message);
    }

    [Fact]
    public async Task MsiUninstall_InvalidProductCodeFormat_ReturnsValidationFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled, productCode: "not-a-guid");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Invalid ProductCode format", result.Error.Message);
    }

    [Fact]
    public async Task MsiUninstall_WrongEntryType_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.ExeInstalled);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("cannot handle", result.Error.Message);
    }

    [Fact]
    public async Task MsiUninstall_ProcessException_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithException(new InvalidOperationException("Process not found"));
        var op = new MsiUninstallOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled,
            productCode: "{12345678-1234-1234-1234-123456789ABC}");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("Process not found", result.Error.Message);
    }

    // --- ExeRollbackOperation Tests ---

    [Fact]
    public void ExeRollback_CanHandle_ExeInstalled_ReturnsTrue()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new ExeRollbackOperation(runner);

        var entry = CreateEntry(JournalEntryType.ExeInstalled);

        Assert.True(op.CanHandle(entry));
    }

    [Fact]
    public void ExeRollback_CanHandle_MsiInstalled_ReturnsFalse()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new ExeRollbackOperation(runner);

        var entry = CreateEntry(JournalEntryType.MsiInstalled);

        Assert.False(op.CanHandle(entry));
    }

    [Fact]
    public async Task ExeRollback_ValidUninstallCommand_ExecutesCorrectly()
    {
        // Create a temp file to act as the executable so it passes existence check
        var tempExe = Path.GetTempFileName();
        try
        {
            var runner = new MockProcessRunner().WithExitCode(0);
            var op = new ExeRollbackOperation(runner);
            var entry = CreateEntry(JournalEntryType.ExeInstalled,
                uninstallCommand: $"\"{tempExe}\" /silent");

            var result = await op.ExecuteAsync(entry, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(tempExe, runner.LastFileName);
            Assert.Equal("/silent", runner.LastArguments);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public async Task ExeRollback_RelativePath_ReturnsValidationFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new ExeRollbackOperation(runner);
        var entry = CreateEntry(JournalEntryType.ExeInstalled,
            uninstallCommand: @"uninstall.exe /quiet /norestart");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("fully qualified", result.Error.Message);
    }

    [Fact]
    public async Task ExeRollback_NonexistentExecutable_ReturnsFileNotFoundFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new ExeRollbackOperation(runner);
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.exe");
        var entry = CreateEntry(JournalEntryType.ExeInstalled,
            uninstallCommand: $"\"{fakePath}\" /silent");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message);
    }

    [Fact]
    public async Task ExeRollback_MissingUninstallCommand_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new ExeRollbackOperation(runner);
        var entry = CreateEntry(JournalEntryType.ExeInstalled, uninstallCommand: null);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("No uninstall command", result.Error.Message);
    }

    [Fact]
    public async Task ExeRollback_NonZeroExitCode_ReturnsFailure()
    {
        var tempExe = Path.GetTempFileName();
        try
        {
            var runner = new MockProcessRunner().WithExitCode(99);
            var op = new ExeRollbackOperation(runner);
            var entry = CreateEntry(JournalEntryType.ExeInstalled,
                uninstallCommand: $"\"{tempExe}\" /silent");

            var result = await op.ExecuteAsync(entry, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("exit code 99", result.Error.Message);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public async Task ExeRollback_WrongEntryType_ReturnsFailure()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var op = new ExeRollbackOperation(runner);
        var entry = CreateEntry(JournalEntryType.MsiInstalled);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("cannot handle", result.Error.Message);
    }

    // --- CacheCleanupOperation Tests ---

    [Fact]
    public void CacheCleanup_CanHandle_PayloadCached_ReturnsTrue()
    {
        var op = new CacheCleanupOperation();

        var entry = CreateEntry(JournalEntryType.PayloadCached);

        Assert.True(op.CanHandle(entry));
    }

    [Fact]
    public void CacheCleanup_CanHandle_MsiInstalled_ReturnsFalse()
    {
        var op = new CacheCleanupOperation();

        var entry = CreateEntry(JournalEntryType.MsiInstalled);

        Assert.False(op.CanHandle(entry));
    }

    [Fact]
    public async Task CacheCleanup_ExistingFile_DeletesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkCache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "cached.msi");
        File.WriteAllText(tempFile, "test");
        try
        {
            var op = new CacheCleanupOperation(tempDir);
            Assert.True(File.Exists(tempFile));

            var entry = CreateEntry(JournalEntryType.PayloadCached, cachePath: tempFile);

            var result = await op.ExecuteAsync(entry, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CacheCleanup_NonexistentFile_ReturnsSuccess()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkCache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var op = new CacheCleanupOperation(tempDir);
            var entry = CreateEntry(JournalEntryType.PayloadCached,
                cachePath: Path.Combine(tempDir, $"nonexistent_{Guid.NewGuid():N}.tmp"));

            var result = await op.ExecuteAsync(entry, CancellationToken.None);

            Assert.True(result.IsSuccess);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CacheCleanup_MissingCachePath_ReturnsFailure()
    {
        var op = new CacheCleanupOperation();
        var entry = CreateEntry(JournalEntryType.PayloadCached, cachePath: null);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.RollbackError, result.Error.Kind);
        Assert.Contains("CachePath", result.Error.Message);
    }

    [Fact]
    public async Task CacheCleanup_RelativePath_ReturnsValidationFailure()
    {
        var op = new CacheCleanupOperation();
        var entry = CreateEntry(JournalEntryType.PayloadCached, cachePath: "relative/path/file.msi");

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("absolute path", result.Error.Message);
    }

    [Fact]
    public async Task CacheCleanup_PathTraversal_ReturnsValidationFailure()
    {
        var allowedRoot = Path.Combine(Path.GetTempPath(), "FalkInstaller", "Cache");
        var op = new CacheCleanupOperation(allowedRoot);
        // Attempt to traverse out of the allowed cache root
        var traversalPath = Path.Combine(allowedRoot, "..", "..", "Windows", "System32", "evil.dll");
        var entry = CreateEntry(JournalEntryType.PayloadCached, cachePath: traversalPath);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("not within an allowed cache directory", result.Error.Message);
    }

    [Fact]
    public async Task CacheCleanup_PathOutsideAllowedRoot_ReturnsValidationFailure()
    {
        var allowedRoot = Path.Combine(Path.GetTempPath(), "FalkInstaller", "Cache");
        var op = new CacheCleanupOperation(allowedRoot);
        var outsidePath = Path.Combine(Path.GetTempPath(), "OtherApp", "file.msi");
        var entry = CreateEntry(JournalEntryType.PayloadCached, cachePath: outsidePath);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("not within an allowed cache directory", result.Error.Message);
    }

    [Fact]
    public async Task CacheCleanup_WrongEntryType_ReturnsFailure()
    {
        var op = new CacheCleanupOperation();
        var entry = CreateEntry(JournalEntryType.MsiInstalled);

        var result = await op.ExecuteAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("cannot handle", result.Error.Message);
    }

    // --- ExeRollbackOperation.ParseCommand Tests ---

    [Fact]
    public void ParseCommand_QuotedPath_WithArguments_ParsesCorrectly()
    {
        var (fileName, arguments) = ExeRollbackOperation.ParseCommand(
            @"""C:\Program Files\App\uninstall.exe"" /quiet /norestart");

        Assert.Equal(@"C:\Program Files\App\uninstall.exe", fileName);
        Assert.Equal("/quiet /norestart", arguments);
    }

    [Fact]
    public void ParseCommand_UnquotedPath_WithArguments_ParsesCorrectly()
    {
        var (fileName, arguments) = ExeRollbackOperation.ParseCommand(
            "uninstall.exe /quiet");

        Assert.Equal("uninstall.exe", fileName);
        Assert.Equal("/quiet", arguments);
    }

    [Fact]
    public void ParseCommand_SingleExecutable_NoArguments()
    {
        var (fileName, arguments) = ExeRollbackOperation.ParseCommand("uninstall.exe");

        Assert.Equal("uninstall.exe", fileName);
        Assert.Equal(string.Empty, arguments);
    }

    // --- Helper methods ---

    private static JournalEntry CreateEntry(
        JournalEntryType entryType,
        string? packageId = "TestPkg",
        string? productCode = null,
        string? uninstallCommand = null,
        string? cachePath = null)
    {
        return new JournalEntry
        {
            EntryType = entryType,
            Description = $"Test entry for {packageId}",
            PackageId = packageId,
            PackageType = entryType == JournalEntryType.MsiInstalled ? "MsiPackage" :
                         entryType == JournalEntryType.ExeInstalled ? "ExePackage" : null,
            ProductCode = productCode,
            UninstallCommand = uninstallCommand,
            CachePath = cachePath
        };
    }
}
