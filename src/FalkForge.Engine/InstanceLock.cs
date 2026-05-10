namespace FalkForge.Engine;

/// <summary>
/// Prevents two concurrent engine instances from running against the same bundle.
/// Without this guard, two simultaneous installs race on the package cache and MSI
/// database, surfacing as opaque MSI error 1618 (another installation is already in progress).
/// </summary>
/// <remarks>
/// Uses a named system Mutex in the Global\ namespace so the lock is machine-wide
/// and survives across session boundaries (e.g. elevation).
/// </remarks>
public static class InstanceLock
{
    /// <summary>
    /// Tries to acquire the per-bundle global mutex.
    /// </summary>
    /// <param name="bundleId">
    /// The unique identifier of the bundle being installed. Used to derive the mutex name.
    /// </param>
    /// <param name="lockHandle">
    /// On success, a disposable handle that releases the mutex when disposed.
    /// <c>null</c> when the method returns <c>false</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the mutex was acquired (this is the only running instance);
    /// <c>false</c> if another instance already holds the mutex.
    /// </returns>
    public static bool TryAcquire(string bundleId, out IDisposable? lockHandle)
    {
        // Sanitize: replace characters not valid in mutex names (backslash reserved for Global\).
        var safeBundleId = bundleId.Replace('\\', '_').Replace('/', '_');

        // Prefer Global\ so the mutex is machine-wide across session boundaries (e.g.
        // standard-user → elevated companion). Fall back to Local\ only when
        // SeCreateGlobalPrivilege is not held (non-elevated processes, sandbox environments).
        // Within a single session Local\ still prevents two concurrent installs of the same bundle.
        try
        {
            return TryAcquireNamed($@"Global\FalkForge_Install_{safeBundleId}", out lockHandle);
        }
        catch (UnauthorizedAccessException)
        {
            // Privilege insufficient for Global\ — fall back to session-local scope.
            return TryAcquireNamed($@"Local\FalkForge_Install_{safeBundleId}", out lockHandle);
        }
    }

    private static bool TryAcquireNamed(string mutexName, out IDisposable? lockHandle)
    {
        // Use a Semaphore(1,1) instead of Mutex so the lock is NOT thread-re-entrant.
        // Windows named Mutex re-enters on the same thread (by design), which means a
        // second TryAcquire call from the same thread would always succeed even when the
        // first handle is still live. Semaphore(1,1) has exactly one permit and blocks
        // every additional WaitOne regardless of which thread calls it.
        Semaphore? sem = null;
        try
        {
            sem = new Semaphore(initialCount: 1, maximumCount: 1, name: mutexName, out _);

            // WaitOne(0) = non-blocking: returns true only if a permit is available.
            var acquired = sem.WaitOne(millisecondsTimeout: 0);
            if (!acquired)
            {
                sem.Dispose();
                lockHandle = null;
                return false;
            }

            lockHandle = new SemaphoreHandle(sem);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Privilege insufficient for this namespace — signal caller to try fallback.
            sem?.Dispose();
            lockHandle = null;
            throw;
        }
        catch
        {
            sem?.Dispose();
            lockHandle = null;
            return false;
        }
    }

    private sealed class SemaphoreHandle : IDisposable
    {
        private readonly Semaphore _semaphore;
        private bool _disposed;

        internal SemaphoreHandle(Semaphore semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _semaphore.Release(); }
            catch { /* already released or semaphore full — defensive */ }
            finally { _semaphore.Dispose(); }
        }
    }
}
