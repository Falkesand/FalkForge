namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Security.Cryptography;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The elevated companion applies an author-declared MSI transform ONLY when its
/// bytes match the publisher-signed set AND the signed package-to-transform association map permits it for
/// the package being installed. A caller-supplied transform on the args wire is still refused.
///
/// <para>The base MSI and the transforms here are arbitrary-byte files, not real MSI databases: the mocked
/// <see cref="IMsiApi"/> records the command line without applying anything, so what these tests prove is
/// the verification-and-merge decision, not msiexec's own transform application. The bytes only have to
/// hash to the value the manifest signs. A companion-generated secret transform (which does need a real
/// database) is exercised by <see cref="MsiInstallCommandSecretTests"/>.</para>
/// </summary>
public sealed class MsiInstallCommandTransformTests : IDisposable
{
    private const string PackageA = "App.Main";
    private const string PackageB = "Other.Main";
    private const string TransformId = "App.Lang.de-DE";

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"MsiInstallTransform_{Guid.NewGuid():N}");
    private readonly MockMsiApi _mockMsiApi = new();
    private readonly ECDsa _publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly MsiInstallCommand _command;

    public MsiInstallCommandTransformTests()
    {
        Directory.CreateDirectory(_tempDir);
        _command = new MsiInstallCommand(
            _mockMsiApi,
            new NoopStaging(),
            SignedManifestPayload.TrustedSet(_publisherKey),
            SignedManifestPayload.NoRoles,
            SignedManifestPayload.NoPqCompanions);
    }

    public void Dispose()
    {
        _publisherKey.Dispose();
        TestTemp.TryDelete(_tempDir);
    }

    // Writes a file with the given bytes and returns (resolved final path, its SHA-256 hex). The path is
    // resolved from an open handle the same way the command resolves it, so an 8.3 short component on a CI
    // runner does not make the test's copy of the path differ from the command's.
    private (string path, string hash) CreateFile(string name, byte[] bytes)
    {
        var raw = Path.Combine(_tempDir, name);
        File.WriteAllBytes(raw, bytes);
        using var stream = File.OpenRead(raw);
        var resolved = HashBoundFile.TryGetFinalPath(stream.SafeFileHandle) ?? raw;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(raw)));
        return (resolved, hash);
    }

    [Fact]
    public void Execute_SignedAssociatedTransform_IsApplied_PathReachesCommandLine()
    {
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var (transformPath, transformHash) = CreateFile("app.mst", "transform-bytes"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [(PackageA, TransformId, transformHash)],
            associations: [(PackageA, [TransformId])],
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
        Assert.NotNull(_mockMsiApi.LastCommandLine);
        Assert.Contains("TRANSFORMS=\"", _mockMsiApi.LastCommandLine, StringComparison.Ordinal);
        // The resolved transform path is what msiexec would read; assert it is the TRANSFORMS value.
        Assert.Contains(transformPath, _mockMsiApi.LastCommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsignedTransform_IsRejected_NoInstall()
    {
        // The forwarded transform id has no signed integrity entry: it is not part of the signed set.
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var (transformPath, _) = CreateFile("app.mst", "transform-bytes"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [],
            associations: [],
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_TamperedTransformBytes_IsRejected_NoInstall()
    {
        // The manifest signs the hash of the intended transform, but the file on disk carries different
        // bytes: the hash-bind must reject it before the install runs.
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var intendedHash = Convert.ToHexString(SHA256.HashData("intended-transform"u8.ToArray()));
        var (transformPath, _) = CreateFile("app.mst", "tampered-transform"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [(PackageA, TransformId, intendedHash)],
            associations: [(PackageA, [TransformId])],
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_CrossPackageTransform_IsRejected_NoInstall()
    {
        // Transform T's bytes match its signed entry, but the signed association map permits it only for
        // package B. Installing package A must be refused: T's bytes must never reach package A's install.
        var (msiPath, msiHashA) = CreateFile("appA.msi", "msi-a-bytes"u8.ToArray());
        var (_, msiHashB) = CreateFile("appB.msi", "msi-b-bytes"u8.ToArray());
        var (transformPath, transformHash) = CreateFile("app.mst", "transform-bytes"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHashA), (PackageB, msiHashB)],
            declaredTransforms: [(PackageB, TransformId, transformHash)],
            associations: [(PackageB, [TransformId])],
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_SignedButUnassociatedTransform_IsRejected_NoInstall()
    {
        // T's bytes match a signed entry and T is declared under package A (so the integrity gate binds it),
        // but no association map entry permits (A, T). The companion must refuse it.
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var (transformPath, transformHash) = CreateFile("app.mst", "transform-bytes"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [(PackageA, TransformId, transformHash)],
            associations: [], // signed, bound by the gate, but not associated with any package
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_TransformPathWithSemicolon_IsRejected_NoInstall()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Uses an NTFS filename containing ';'.");

        // NTFS allows ';' in a filename; msiexec splits TRANSFORMS on ';'. A ';'-bearing resolved path
        // would smuggle a second transform, so it must be refused even when the bytes verify.
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var (transformPath, transformHash) = CreateFile("app;evil.mst", "transform-bytes"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [(PackageA, TransformId, transformHash)],
            associations: [(PackageA, [TransformId])],
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_ArgsWireTransforms_StillRejected_EvenWithValidForwardedTransform()
    {
        // A TRANSFORMS property on the ARGS wire is rejected before the forwarded-transform
        // step runs, regardless of whether a validly signed+associated transform also rides the request.
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var (transformPath, transformHash) = CreateFile("app.mst", "transform-bytes"u8.ToArray());

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [(PackageA, TransformId, transformHash)],
            associations: [(PackageA, [TransformId])],
            _publisherKey);

        var payload = SignedManifestPayload.Build(
            msiPath, " TRANSFORMS=\"C:\\attacker\\evil.mst\"", PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_HeldHandle_PinsTransformBytesAcrossInstall()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("FileShare.Read write-deny semantics are exercised on Windows.");

        // The companion holds a FileShare.Read handle on the verified transform for the whole install, so
        // the bytes msiexec reads from disk are the bytes just hashed. Prove it: at the moment InstallProduct
        // runs, a write-open of the transform is denied (sharing violation) while a read-open still sees the
        // signed bytes. A same-user attacker therefore cannot swap the .mst after the hash check.
        var (msiPath, msiHash) = CreateFile("app.msi", "msi-bytes"u8.ToArray());
        var signedBytes = "signed-transform-bytes"u8.ToArray();
        var (transformPath, transformHash) = CreateFile("app.mst", signedBytes);

        var manifest = SignedManifestPayload.ManifestJson(
            packages: [(PackageA, msiHash)],
            declaredTransforms: [(PackageA, TransformId, transformHash)],
            associations: [(PackageA, [TransformId])],
            _publisherKey);

        var writeWasDenied = false;
        byte[]? bytesReadableAtInstall = null;
        _mockMsiApi.OnInstallProductCalled = () =>
        {
            try
            {
                using var write = new FileStream(transformPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                write.Write("attacker-swap"u8);
            }
            catch (IOException)
            {
                writeWasDenied = true;
            }

            // A reader may still open it (FileShare.Read), and it still holds the signed bytes.
            using var read = new FileStream(transformPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ms = new MemoryStream();
            read.CopyTo(ms);
            bytesReadableAtInstall = ms.ToArray();
        };

        var payload = SignedManifestPayload.Build(
            msiPath, string.Empty, PackageA, manifest,
            secrets: null, transforms: [(TransformId, transformPath)]);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
        Assert.True(writeWasDenied, "the held handle must deny a concurrent write-open of the transform");
        Assert.Equal(signedBytes, bytesReadableAtInstall);
    }
}
