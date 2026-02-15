namespace FalkInstaller.Engine.Journal.UndoOperations;

public sealed class CacheCleanupOperation : IUndoOperation
{
    private static readonly string DefaultProgramDataCacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FalkInstaller", "Cache");

    private static readonly string DefaultLocalAppDataCacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FalkInstaller", "Cache");

    private readonly string[] _allowedCacheRoots;

    public CacheCleanupOperation()
        : this(DefaultProgramDataCacheRoot, DefaultLocalAppDataCacheRoot)
    {
    }

    public CacheCleanupOperation(params string[] allowedCacheRoots)
    {
        _allowedCacheRoots = allowedCacheRoots;
    }

    public bool CanHandle(JournalEntry entry) => entry.EntryType == JournalEntryType.PayloadCached;

    public Task<Result<Unit>> ExecuteAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.EntryType != JournalEntryType.PayloadCached)
        {
            return Task.FromResult(Result<Unit>.Failure(ErrorKind.RollbackError,
                $"CacheCleanupOperation cannot handle entry type {entry.EntryType}"));
        }

        var cachePath = entry.CachePath;
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            return Task.FromResult(Result<Unit>.Failure(ErrorKind.RollbackError,
                $"CachePath is required for cache cleanup of package '{entry.PackageId}'"));
        }

        if (!Path.IsPathFullyQualified(cachePath))
        {
            return Task.FromResult(Result<Unit>.Failure(ErrorKind.Validation,
                $"CachePath must be an absolute path, got: '{cachePath}'"));
        }

        // Resolve symlinks and relative segments to canonical form
        var normalizedPath = Path.GetFullPath(cachePath);

        // Reject paths that contained traversal sequences (e.g., "..")
        if (normalizedPath.Contains("..", StringComparison.Ordinal))
        {
            return Task.FromResult(Result<Unit>.Failure(ErrorKind.Validation,
                $"CachePath contains path traversal sequences: '{cachePath}'"));
        }

        // Validate the path is within an allowed cache root
        var isContained = false;
        foreach (var root in _allowedCacheRoots)
        {
            var normalizedRoot = Path.GetFullPath(root);
            if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                normalizedRoot += Path.DirectorySeparatorChar;
            }

            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                isContained = true;
                break;
            }
        }

        if (!isContained)
        {
            return Task.FromResult(Result<Unit>.Failure(ErrorKind.Validation,
                $"CachePath '{cachePath}' is not within an allowed cache directory"));
        }

        try
        {
            if (File.Exists(normalizedPath))
            {
                File.Delete(normalizedPath);
            }

            return Task.FromResult<Result<Unit>>(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Result<Unit>.Failure(ErrorKind.RollbackError,
                $"Failed to delete cached file '{cachePath}': {ex.Message}"));
        }
    }
}
