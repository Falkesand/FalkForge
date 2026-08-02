using Xunit;

namespace FalkForge.Core.Tests;

/// <summary>
/// <see cref="TestTemp.TryDelete"/> is the single shared teardown helper that replaced 265+
/// duplicated inline `try { Directory.Delete(...) } catch (Exception ex) when (...) { }` blocks
/// across the test suite. It must delete real directories, never throw regardless of why the
/// delete failed (a locked handle, a transient I/O error, or `UnauthorizedAccessException` are
/// all "someone else is cleaning up the test's temp folder", not a reason to mask the test's
/// real assertion result), and it must not go completely silent on failure -- that traded one
/// invisible bug (escaping teardown exceptions) for another (blind cleanup failures), so a
/// failed delete has to leave a trace a human reading test output can actually see.
/// </summary>
public sealed class TestTempTests : IDisposable
{
    private readonly string _root;

    public TestTempTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"TestTempTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Deliberately not using TestTemp.TryDelete here -- this class exists to verify that
        // helper's behavior, so its own teardown stays a plain best-effort delete to avoid
        // circular reasoning about the thing under test.
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void TryDelete_ExistingDirectoryWithContents_DeletesItRecursively()
    {
        var target = Path.Combine(_root, "existing");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "file.txt"), "content");

        TestTemp.TryDelete(target);

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void TryDelete_PathDoesNotExist_DoesNotThrow()
    {
        var target = Path.Combine(_root, "never-existed");

        var exception = Record.Exception(() => TestTemp.TryDelete(target));

        Assert.Null(exception);
    }

    [Fact]
    public void TryDelete_DirectoryContainsOpenHandle_SwallowsTheGenuineDeletionFailure()
    {
        // A real open file handle inside the directory, not a mocked filesystem -- this is the
        // exact case the original narrow `catch (IOException)` bug this helper fixes was blind
        // to on Windows (UnauthorizedAccessException from a locked file, not IOException).
        var target = Path.Combine(_root, "locked");
        Directory.CreateDirectory(target);
        var lockedFile = Path.Combine(target, "locked.bin");

        using var handle = new FileStream(
            lockedFile, FileMode.Create, FileAccess.Write, FileShare.None);
        handle.Write([1, 2, 3]);
        handle.Flush();

        var exception = Record.Exception(() => TestTemp.TryDelete(target));

        Assert.Null(exception);
        // The delete genuinely failed (handle still open) -- prove TryDelete didn't silently
        // pretend to succeed.
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void TryDelete_DeletionFails_LeavesATraceOnStandardError()
    {
        var target = Path.Combine(_root, "locked-for-trace");
        Directory.CreateDirectory(target);
        var lockedFile = Path.Combine(target, "locked.bin");

        var originalErr = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            using var handle = new FileStream(
                lockedFile, FileMode.Create, FileAccess.Write, FileShare.None);

            TestTemp.TryDelete(target);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var trace = captured.ToString();
        Assert.Contains(target, trace, StringComparison.Ordinal);
    }
}
