using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Decompiler.Tests;

internal sealed class MockBundleAccess : IBundleAccess
{
    private Result<InstallerManifest>? _manifest;
    private Result<TocEntry[]>? _toc;

    public MockBundleAccess WithManifest(InstallerManifest manifest)
    {
        _manifest = Result<InstallerManifest>.Success(manifest);
        return this;
    }

    public MockBundleAccess WithManifestFailure(ErrorKind kind, string message)
    {
        _manifest = Result<InstallerManifest>.Failure(kind, message);
        return this;
    }

    public MockBundleAccess WithToc(params TocEntry[] entries)
    {
        _toc = Result<TocEntry[]>.Success(entries);
        return this;
    }

    public MockBundleAccess WithTocFailure(ErrorKind kind, string message)
    {
        _toc = Result<TocEntry[]>.Failure(kind, message);
        return this;
    }

    public Result<InstallerManifest> ReadManifest()
    {
        return _manifest ?? Result<InstallerManifest>.Failure(ErrorKind.BundleError, "No manifest configured in mock.");
    }

    public Result<TocEntry[]> ReadToc()
    {
        return _toc ?? Result<TocEntry[]>.Failure(ErrorKind.BundleError, "No TOC configured in mock.");
    }

    public void Dispose()
    {
        // No-op for mock
    }
}
