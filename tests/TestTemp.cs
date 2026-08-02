using System.IO;

namespace FalkForge.TestSupport;

/// <summary>
/// Best-effort temp-directory cleanup for test teardown, shared by every test project (linked in
/// via <c>tests/Directory.Build.props</c> so no project reference is needed).
/// <para>
/// Teardown/<see cref="IDisposable.Dispose"/> deleting a temp directory must never let a locked
/// handle, a transient I/O error, or a permissions denial escape as an unhandled exception --
/// doing so masks the test's real assertion result behind an unrelated teardown crash. A narrow
/// <c>catch (IOException)</c> is not enough: <see cref="UnauthorizedAccessException"/> is the
/// common case on Windows (antivirus scanning, a lingering handle) that it misses.
/// </para>
/// <para>
/// Swallowing the failure entirely would trade one silent problem for another -- this repo has
/// already suffered real temp-root accumulation from cleanup that failed with no trace. So a
/// genuine deletion failure is not silently discarded: it is logged to <see cref="Console.Error"/>
/// (one line naming the path and the exception type) so a human reading test output still sees
/// it. Failures here should be rare; if this ever gets noisy, that noise is itself the signal.
/// </para>
/// </summary>
public static class TestTemp
{
    /// <summary>
    /// Recursively deletes <paramref name="path"/> if it exists. Never throws, except for
    /// <see cref="OutOfMemoryException"/> or <see cref="StackOverflowException"/>, which are
    /// genuinely unrecoverable and must be allowed to propagate.
    /// </summary>
    /// <param name="path">The directory to delete, if it exists.</param>
    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine(
                $"[TestTemp.TryDelete] best-effort cleanup failed for '{path}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
