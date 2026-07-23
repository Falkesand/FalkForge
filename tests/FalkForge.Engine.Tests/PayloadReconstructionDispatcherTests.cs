namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Protocol.Bundle;
using Xunit;

/// <summary>
/// Covers <see cref="PayloadReconstructionDispatcher.Dispatch"/>'s fail-loud branches (GAP-1): a
/// delta payload with no base bundle, or a base bundle that does not exist, must never fall through
/// to writing raw/unreconstructed bytes — the honest recovery is always "download the full
/// installer instead". These branches decide whether a delta update can proceed at all, so each is
/// asserted on both <see cref="Error.Kind"/> and the message text (they share
/// <see cref="ErrorKind.BundleError"/>, so message text is what a caller/log reader actually uses
/// to tell the two apart).
///
/// The non-delta passthrough branch (<c>!entry.IsDelta</c>) and the delegate-to-DeltaApplicator
/// happy path are NOT covered here: both require a real, fully-formed bundle file (built by
/// FalkForge.Compiler.Bundle's writer), which this test project does not reference and building one
/// by hand is not cheap. The passthrough is a one-line call straight into
/// <see cref="BundleReader.ExtractPayloadToFile(string, TocEntry, string, string)"/>, already
/// exercised by BundleReader's own tests; the happy-path delegate is a one-line call straight into
/// <see cref="DeltaApplicator.ReconstructPayloadToFile(string, TocEntry, string, string, string)"/>,
/// already exercised by DeltaApplicatorTests (FalkForge.Compiler.Bundle.Tests). Both are covered
/// transitively.
/// </summary>
public sealed class PayloadReconstructionDispatcherTests : IDisposable
{
    private readonly string _tempDir;

    public PayloadReconstructionDispatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests_PayloadReconstructionDispatcher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static TocEntry MakeDeltaEntry() => new()
    {
        PackageId = "MyPackage",
        Offset = 0,
        CompressedSize = 16,
        OriginalSize = 32,
        Sha256Hash = "deadbeef",
        IsDelta = true,
        BaseSha256Hash = "basehash",
        ReconstructedSha256Hash = "reconstructedhash"
    };

    [Fact]
    public void Dispatch_DeltaEntryWithNullBaseBundle_FailsLoud_SoUpdateCannotProceedBlind()
    {
        var entry = MakeDeltaEntry();

        var result = PayloadReconstructionDispatcher.Dispatch(
            bundlePath: "unused.bundle", entry, _tempDir, "MyPackage/MyPackage.dat", baseBundlePath: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("MyPackage", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("no base bundle is available", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("--base-bundle", result.Error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_tempDir, "MyPackage", "MyPackage.dat")));
    }

    [Fact]
    public void Dispatch_DeltaEntryWithWhitespaceOnlyBaseBundle_FailsLoud_ViaFileNotFoundBranch()
    {
        // Code guard is string.IsNullOrEmpty, not IsNullOrWhiteSpace — a whitespace-only path does
        // NOT match the "no base supplied" branch (b); it falls through to the File.Exists check
        // and fails there instead (File.Exists returns false for a whitespace-only path). Still
        // fail-loud either way, just via the (c) branch/message rather than (b)'s — asserting the
        // real branch here pins that behavior instead of assuming (b) fires.
        var entry = MakeDeltaEntry();

        var result = PayloadReconstructionDispatcher.Dispatch(
            bundlePath: "unused.bundle", entry, _tempDir, "MyPackage/MyPackage.dat", baseBundlePath: "   ");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("was not found", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no base bundle is available", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_DeltaEntryWithEmptyBaseBundle_FailsLoud_SoUpdateCannotProceedBlind()
    {
        var entry = MakeDeltaEntry();

        var result = PayloadReconstructionDispatcher.Dispatch(
            bundlePath: "unused.bundle", entry, _tempDir, "MyPackage/MyPackage.dat", baseBundlePath: string.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("no base bundle is available", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_DeltaEntryWithMissingBaseBundleFile_FailsLoud_DistinctFromNoBaseSupplied()
    {
        var entry = MakeDeltaEntry();
        var missingBasePath = Path.Combine(_tempDir, "does-not-exist.bundle");

        var result = PayloadReconstructionDispatcher.Dispatch(
            bundlePath: "unused.bundle", entry, _tempDir, "MyPackage/MyPackage.dat", baseBundlePath: missingBasePath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("MyPackage", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("was not found", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("does-not-exist.bundle", result.Error.Message, StringComparison.Ordinal);
        // Distinct wording from the "no base supplied" branch — a caller/log reader must be able
        // to tell "you never passed --base-bundle" apart from "you passed one but it's gone".
        Assert.DoesNotContain("no base bundle is available", result.Error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_tempDir, "MyPackage", "MyPackage.dat")));
    }
}
