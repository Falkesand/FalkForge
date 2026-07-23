using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Bundle;

/// <summary>
/// End-to-end tests for the install-time half of delta updates: building a v1 full bundle and a
/// v2 delta bundle with the real compilers, then driving <see cref="DeltaApplicator"/> — the
/// engine-side reconstruction path — to prove a delta payload is reconstructed to the exact v2
/// bytes (and rejected loudly, writing no output, whenever the basis is wrong/missing or the
/// reconstructed content does not match its declared hash).
///
/// Before this type existed, the runtime extraction path (BundleReader) gzip-decompressed a delta
/// payload and verified only the delta-blob hash, writing the raw Octodiff delta blob to the
/// destination as if it were the finished payload — silent garbage. These tests encode that the
/// finished, reconstructed payload (verified against <see cref="TocEntry.ReconstructedSha256Hash"/>)
/// is what lands on disk.
/// </summary>
public sealed class DeltaApplicatorTests : IDisposable
{
    private readonly string _tempDir;

    public DeltaApplicatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DeltaApply_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ReconstructPayloadToFile_ValidDelta_ProducesByteExactNewPayload()
    {
        var (basisBundle, deltaBundle, _, newData, deltaEntry) = BuildV1AndDelta();

        var destDir = Path.Combine(_tempDir, "out_ok");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, basisBundle, destDir, deltaEntry.PackageId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(File.Exists(result.Value), "Reconstructed payload file should exist");
        Assert.Equal(newData, File.ReadAllBytes(result.Value));
        Assert.Equal(deltaEntry.ReconstructedSha256Hash,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Value))), ignoreCase: true);
    }

    [Fact]
    public void ReconstructPayloadToFile_BasisBundleMissingPackage_FailsLoud_WritesNoOutput()
    {
        var (_, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A basis bundle that does not contain the delta entry's package at all.
        var unrelated = BuildFullBundle("unrelated", RandomBytes(4_000, seed: 99), "SomeOtherPkg");

        var destDir = Path.Combine(_tempDir, "out_missing");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, unrelated, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the basis payload is absent");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_WrongBaseVersion_FailsLoud_WritesNoOutput()
    {
        var (_, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A basis bundle with the SAME package id but DIFFERENT bytes: its payload hash will not
        // match the delta's declared BaseSha256Hash, so it is the wrong base version.
        var wrongBase = BuildFullBundle("wrongbase", RandomBytes(10_000, seed: 123), deltaEntry.PackageId);

        var destDir = Path.Combine(_tempDir, "out_wrongbase");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, wrongBase, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the basis is the wrong base version");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_TamperedReconstructedHash_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // Same delta + correct basis, but the entry's declared reconstructed hash is corrupted.
        // Reconstruction produces the correct bytes, so the ONLY gate that can catch this is the
        // final ReconstructedSha256Hash check — this proves that gate is enforced.
        var tampered = CloneWithReconstructedHash(deltaEntry,
            "0000000000000000000000000000000000000000000000000000000000000000");

        var destDir = Path.Combine(_tempDir, "out_tampered");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, tampered, basisBundle, destDir, tampered.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the reconstructed hash does not match");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_EntryNotMarkedAsDelta_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A TOC entry that isn't flagged IsDelta must never be routed through DeltaApplicator —
        // this is the dispatch boundary between the full-payload extraction path and delta
        // reconstruction; misrouting it would (at best) apply a delta algorithm to a full
        // payload's bytes, producing garbage.
        var notDelta = CloneWithIsDelta(deltaEntry, isDelta: false);

        var destDir = Path.Combine(_tempDir, "out_notdelta");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, notDelta, basisBundle, destDir, notDelta.PackageId);

        Assert.True(result.IsFailure, "DeltaApplicator must refuse an entry not marked IsDelta");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_MissingBaseHashMetadata_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A malformed delta bundle whose TOC never recorded which base payload the delta was
        // computed against — nothing to pin the supplied basis to, so reconstruction must refuse
        // rather than silently trusting whatever basis bundle the caller happens to pass in.
        var missingBaseHash = CloneWithBaseHash(deltaEntry, string.Empty);

        var destDir = Path.Combine(_tempDir, "out_missingbasehash");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, missingBaseHash, basisBundle, destDir, missingBaseHash.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when BaseSha256Hash metadata is missing");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_MissingReconstructedHashMetadata_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // No ReconstructedSha256Hash means there is no final integrity gate to check the applied
        // delta against — refuse up front rather than reconstructing a payload nothing can ever
        // verify. Distinct from the TamperedReconstructedHash test above, which exercises the
        // gate itself with a present-but-wrong hash; this exercises the missing-metadata guard
        // that runs before any reconstruction work starts.
        var missingReconHash = CloneWithReconstructedHash(deltaEntry, string.Empty);

        var destDir = Path.Combine(_tempDir, "out_missingreconhash");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, missingReconHash, basisBundle, destDir, missingReconHash.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when ReconstructedSha256Hash metadata is missing");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_DeltaBlobCorruptedOnDisk_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // Flip bytes inside the delta bundle at the delta blob's own offset (TOC/footer left
        // untouched) — simulates bit rot or a tampered delta payload itself, as opposed to a
        // tampered/wrong basis. BundleReader's own SHA-256 check over the delta blob (verifying
        // TocEntry.Sha256Hash — the delta-blob hash) must catch this before Octodiff ever sees
        // the bytes.
        CorruptBytesAtOffset(deltaBundle, deltaEntry);

        var destDir = Path.Combine(_tempDir, "out_deltacorrupt");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, basisBundle, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the delta blob itself is corrupted");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_DeltaBlobNotValidOctodiffFormat_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // Bytes that pass BundleReader's own SHA-256 check (the cloned entry below declares the
        // hash of THESE exact bytes) but are not a valid Octodiff delta stream at all — distinct
        // from DeltaBlobCorruptedOnDisk above, which flips bytes so the hash itself no longer
        // matches and BundleReader rejects it before Octodiff ever runs. Here the blob is
        // well-formed enough to pass hash verification (e.g. a delta built by a mismatched/buggy
        // pipeline) but Octodiff's BinaryDeltaReader.EnsureMetadata throws
        // Octodiff.Core.CorruptFileFormatException when it parses the payload — a type that
        // derives directly from System.Exception with no shared base with the
        // IOException/InvalidDataException/InvalidOperationException family ApplyDelta already
        // caught, so it used to escape unhandled instead of producing a fail-loud Result.
        var garbage = "not an octodiff delta blob"u8.ToArray();
        var garbageHash = Convert.ToHexString(SHA256.HashData(garbage));
        ReplaceCompressedPayloadBytes(deltaBundle, deltaEntry, garbage);
        var malformed = CloneWithSha256Hash(deltaEntry, garbageHash);

        var destDir = Path.Combine(_tempDir, "out_malformedoctodiff");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, malformed, basisBundle, destDir, malformed.PackageId);

        Assert.True(result.IsFailure,
            "Reconstruction must fail loudly (not throw) when the delta blob is not a valid Octodiff stream");
        // Pin the fail-loud contract precisely: the widened catch in ApplyDelta must surface as
        // ErrorKind.BundleError with its documented "Delta application failed" prefix, not just
        // "some failure Result" — a caller distinguishing this from, say, IntegrityError needs
        // the Kind to be stable, and the message prefix is what a support engineer greps logs for.
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.StartsWith("Delta application failed:", result.Error.Message, StringComparison.Ordinal);
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_BasisPayloadCorruptedOnDisk_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        var basisExtract = BundleReader.Extract(basisBundle);
        Assert.True(basisExtract.IsSuccess, basisExtract.IsFailure ? basisExtract.Error.Message : "");
        var basisEntry = basisExtract.Value.TocEntries.Single(e => e.PackageId == deltaEntry.PackageId);

        // Flip bytes inside the BASIS bundle itself — not swap in a different, valid basis (that
        // is the WrongBaseVersion test above). Here the basis bundle's own TOC still claims the
        // original (correct) hash, but the bytes on disk no longer match it: corruption of the
        // exact right version. BundleReader's SHA-256 check on the basis payload must catch this
        // when DeltaApplicator extracts it — a different code path than the explicit
        // BaseSha256Hash pin comparison, which never even sees a mismatch here.
        CorruptBytesAtOffset(basisBundle, basisEntry);

        var destDir = Path.Combine(_tempDir, "out_basiscorrupt");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, basisBundle, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the basis payload bytes are corrupted");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_FailureDoesNotClobberExistingDestinationFile()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        var tampered = CloneWithReconstructedHash(deltaEntry,
            "0000000000000000000000000000000000000000000000000000000000000000");

        var destDir = Path.Combine(_tempDir, "out_preexisting");
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, tampered.PackageId);
        var staleBytes = Encoding.UTF8.GetBytes("stale previously-installed payload — must survive a failed update");
        File.WriteAllBytes(destPath, staleBytes);

        // Atomic-publish contract: a failed reconstruction (integrity mismatch here) must never
        // touch a payload already sitting at the destination — a failed update leaves the install
        // on its last known-good version instead of wiping or half-writing it.
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, tampered, basisBundle, destDir, tampered.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly on a reconstructed-hash mismatch");
        Assert.Equal(staleBytes, File.ReadAllBytes(destPath));
    }

    [Fact]
    public void ReconstructPayloadToFile_BasisBundlePathDoesNotExist_FailsLoud_WritesNoOutput()
    {
        var (_, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // The basis bundle path itself does not exist on disk — distinct from
        // BasisBundleMissingPackage above (a real, readable bundle that just lacks this
        // package's TOC entry). This exercises the BundleReader.Extract(basisBundlePath)
        // failure guard directly, before the TOC is even consulted. A delta update whose basis
        // was never downloaded/staged must fail loudly, not throw or silently no-op.
        var missingBasisPath = Path.Combine(_tempDir, $"nonexistent_basis_{Guid.NewGuid():N}.falkbundle");

        var destDir = Path.Combine(_tempDir, "out_basisnotfound");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, missingBasisPath, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the basis bundle path does not exist");
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_BasisBundleFileIsNotAValidBundle_FailsLoud_WritesNoOutput()
    {
        var (_, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A file exists at the basis path, but it is arbitrary garbage bytes — no FALKBUNDLE
        // magic/footer at all. Distinct from the nonexistent-path case above: a truncated or
        // corrupted basis download (partial write, disk corruption) must be rejected by
        // BundleReader.Extract just as loudly as a missing file, never parsed as if valid.
        var garbageBasisPath = Path.Combine(_tempDir, "garbage_basis.falkbundle");
        File.WriteAllBytes(garbageBasisPath, RandomBytes(512, seed: 77));

        var destDir = Path.Combine(_tempDir, "out_basisgarbage");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, garbageBasisPath, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure,
            "Reconstruction must fail loudly when the basis bundle file is not a valid bundle");
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_TraversalRelativeDestination_IsRejectedAsSecurityError_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // The public overload's relativeDestination is untrusted in real callers (derived from
        // the delta TOC's PackageId, same as BundleReader.ExtractPayloadToFile) — a crafted
        // "..\..\evil.dll" must be rejected by ContainedPathResolver's containment check before
        // any reconstruction work (basis extraction, Octodiff apply) ever runs, the same
        // choke-point contract proven for the full-payload path in
        // BundleReaderContainmentTests.ExtractPayloadToFile_TraversalPackageId_IsRejectedAndNothingWrittenOutside.
        var hostileRelativeDestination = Path.Combine("..", "..", "evil.dll");
        var escapedTargetPath = Path.GetFullPath(Path.Combine(_tempDir, "out_zipslip", hostileRelativeDestination));

        var destDir = Path.Combine(_tempDir, "out_zipslip");
        Directory.CreateDirectory(destDir);
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, basisBundle, destDir, hostileRelativeDestination);

        Assert.True(result.IsFailure, "Reconstruction must reject a traversal relative destination");
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(escapedTargetPath), "Nothing may be written outside the destination directory");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_FailedReconstruction_LeavesNoLeftoverTempFilesBesideDestination()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // Reaches the point where all three per-attempt temp files (*.basis.tmp / *.delta.tmp /
        // *.recon.tmp) have been created — basis + delta both extract and Octodiff applies
        // cleanly — before failing at the final ReconstructedSha256Hash gate. This is an
        // explicit contract test on the `finally` block's TryDeleteFile calls: a failed
        // reconstruction must not leak temp files beside the destination on every failed
        // update attempt, not merely "no output file" (AssertNoFileWritten alone would pass
        // even if cleanup silently failed to delete a temp file, since none of these three
        // extensions is the published destination file).
        var tampered = CloneWithReconstructedHash(deltaEntry,
            "0000000000000000000000000000000000000000000000000000000000000000");

        var destDir = Path.Combine(_tempDir, "out_tempcleanup");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, tampered, basisBundle, destDir, tampered.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the reconstructed hash does not match");
        Assert.Empty(Directory.GetFiles(destDir, "*.basis.tmp"));
        Assert.Empty(Directory.GetFiles(destDir, "*.delta.tmp"));
        Assert.Empty(Directory.GetFiles(destDir, "*.recon.tmp"));
    }

    /// <summary>
    /// Builds a v1 full bundle and a v2 delta bundle (against v1) for a single package, returning
    /// the basis (v1) bundle path, the delta bundle path, the v1 bytes, the v2 bytes, and the delta
    /// TOC entry from the delta bundle.
    /// </summary>
    private (string BasisBundle, string DeltaBundle, byte[] OldData, byte[] NewData, TocEntry DeltaEntry)
        BuildV1AndDelta()
    {
        const string packageId = "AppPkg";

        var oldData = new byte[20_000];
        Array.Fill(oldData, (byte)'X');
        // Sprinkle structure so the delta is meaningfully smaller than a full re-embed.
        for (var i = 0; i < oldData.Length; i += 64)
            oldData[i] = (byte)(i % 251);

        var basisBundle = BuildFullBundle("v1", oldData, packageId);

        var newData = (byte[])oldData.Clone();
        for (var i = 0; i < 40; i++)
            newData[i * 97 % newData.Length] ^= 0xFF;
        var newPayloadPath = WritePayload(newData, "v2_payload.bin");

        var model = CreateModel("DeltaApp", packageId, newPayloadPath);
        var outputDir = Path.Combine(_tempDir, "v2_delta");
        var deltaResult = new DeltaBundleCompiler { AllowPlaceholderStub = true }.Compile(model, outputDir, basisBundle);
        Assert.True(deltaResult.IsSuccess, deltaResult.IsFailure ? deltaResult.Error.Message : "");

        var extract = BundleReader.Extract(deltaResult.Value);
        Assert.True(extract.IsSuccess, extract.IsFailure ? extract.Error.Message : "");
        var deltaEntry = extract.Value.TocEntries.Single(e => e.PackageId == packageId);
        Assert.True(deltaEntry.IsDelta, "Test setup requires the entry to actually be a delta");

        return (basisBundle, deltaResult.Value, oldData, newData, deltaEntry);
    }

    private string BuildFullBundle(string name, byte[] payloadData, string packageId)
    {
        var payloadPath = WritePayload(payloadData, $"{name}_payload.bin");
        var model = CreateModel(name, packageId, payloadPath);
        var outputDir = Path.Combine(_tempDir, $"{name}_bundle");
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    private string WritePayload(byte[] data, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] RandomBytes(int count, int seed)
    {
        var data = new byte[count];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static void AssertNoFileWritten(string destDir)
    {
        if (!Directory.Exists(destDir))
            return;
        Assert.Empty(Directory.GetFiles(destDir, "*", SearchOption.AllDirectories));
    }

    private static TocEntry CloneWithReconstructedHash(TocEntry entry, string reconstructedHash) => new()
    {
        PackageId = entry.PackageId,
        Offset = entry.Offset,
        CompressedSize = entry.CompressedSize,
        OriginalSize = entry.OriginalSize,
        Sha256Hash = entry.Sha256Hash,
        IsDelta = entry.IsDelta,
        BaseSha256Hash = entry.BaseSha256Hash,
        ReconstructedSha256Hash = reconstructedHash,
        IsPreUI = entry.IsPreUI
    };

    private static TocEntry CloneWithIsDelta(TocEntry entry, bool isDelta) => new()
    {
        PackageId = entry.PackageId,
        Offset = entry.Offset,
        CompressedSize = entry.CompressedSize,
        OriginalSize = entry.OriginalSize,
        Sha256Hash = entry.Sha256Hash,
        IsDelta = isDelta,
        BaseSha256Hash = entry.BaseSha256Hash,
        ReconstructedSha256Hash = entry.ReconstructedSha256Hash,
        IsPreUI = entry.IsPreUI
    };

    private static TocEntry CloneWithSha256Hash(TocEntry entry, string sha256Hash) => new()
    {
        PackageId = entry.PackageId,
        Offset = entry.Offset,
        CompressedSize = entry.CompressedSize,
        OriginalSize = entry.OriginalSize,
        Sha256Hash = sha256Hash,
        IsDelta = entry.IsDelta,
        BaseSha256Hash = entry.BaseSha256Hash,
        ReconstructedSha256Hash = entry.ReconstructedSha256Hash,
        IsPreUI = entry.IsPreUI
    };

    private static TocEntry CloneWithBaseHash(TocEntry entry, string? baseHash) => new()
    {
        PackageId = entry.PackageId,
        Offset = entry.Offset,
        CompressedSize = entry.CompressedSize,
        OriginalSize = entry.OriginalSize,
        Sha256Hash = entry.Sha256Hash,
        IsDelta = entry.IsDelta,
        BaseSha256Hash = baseHash,
        ReconstructedSha256Hash = entry.ReconstructedSha256Hash,
        IsPreUI = entry.IsPreUI
    };

    /// <summary>
    /// Flips a few bytes at <paramref name="entry"/>'s own offset directly on disk (TOC/footer
    /// untouched), so the entry's declared metadata (offset/size/hash) still points at the
    /// corrupted bytes — mirrors <c>DeltaBundleCompilerTests.CorruptOldPayload</c>.
    /// </summary>
    private static void CorruptBytesAtOffset(string bundlePath, TocEntry entry)
    {
        var bytes = File.ReadAllBytes(bundlePath);
        var start = (int)entry.Offset;
        for (var i = 0; i < 8 && start + i < bytes.Length; i++)
            bytes[start + i] ^= 0xFF;
        File.WriteAllBytes(bundlePath, bytes);
    }

    /// <summary>
    /// Gzip-compresses <paramref name="rawContent"/> and overwrites <paramref name="entry"/>'s
    /// payload slot (its own offset, within its declared compressed-size bound) in
    /// <paramref name="bundlePath"/> with it — same offset/size metadata, entirely different
    /// decompressed bytes underneath. Callers pair this with a cloned <see cref="TocEntry"/>
    /// whose <see cref="TocEntry.Sha256Hash"/> matches <paramref name="rawContent"/>, so
    /// BundleReader's own integrity check passes and whatever consumes the decompressed bytes
    /// next (Octodiff, here) is the one that has to reject them.
    /// </summary>
    private static void ReplaceCompressedPayloadBytes(string bundlePath, TocEntry entry, byte[] rawContent)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(rawContent, 0, rawContent.Length);
        var compressedBytes = compressed.ToArray();

        Assert.True(compressedBytes.Length <= entry.CompressedSize,
            "Test fixture bug: replacement payload compresses larger than the slot it must fit in");

        var bytes = File.ReadAllBytes(bundlePath);
        var start = (int)entry.Offset;
        Array.Copy(compressedBytes, 0, bytes, start, compressedBytes.Length);
        File.WriteAllBytes(bundlePath, bytes);
    }

    private static BundleModel CreateModel(string name, string packageId, string sourcePath)
    {
        var package = new BundlePackageModel
        {
            Id = packageId,
            SourcePath = sourcePath,
            Type = BundlePackageType.MsiPackage,
            DisplayName = packageId
        };

        return new BundleModel
        {
            Name = name,
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new[] { package }.AsReadOnly()
        };
    }
}
