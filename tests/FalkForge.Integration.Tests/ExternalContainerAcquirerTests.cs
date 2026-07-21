using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// A6 Stage 2 — the engine's runtime acquisition of an external/downloadable container. A real bundle is
/// compiled with one embedded payload and one external-container payload (Stage 1), then
/// <see cref="ExternalContainerAcquirer"/> is driven with a faithful in-process download seam that serves
/// the actual compiled container file. The genuine live-network download is out of scope for a unit test
/// (needs a real URL/host); this pins the verify → membership → signed-set-bind → extract logic and the
/// fail-loud behavior when integrity is violated.
/// </summary>
public sealed class ExternalContainerAcquirerTests : IDisposable
{
    private const string ExternalPackageId = "ExternalApp";
    private const string EmbeddedPackageId = "EmbeddedApp";
    private const string ContainerId = "cdn";
    private const string ContainerUrl = "https://cdn.example.com/app.ffcontainer";

    private readonly string _tempDir;
    private readonly byte[] _externalBytes = [0xD0, 0xCF, 0x11, 0xE0, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x42];

    public ExternalContainerAcquirerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ExtAcquire_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Compiles the fixture bundle and returns (manifest, containerFilePath).</summary>
    private (InstallerManifest Manifest, string ContainerPath) CompileFixture(string outName)
    {
        var embeddedPath = Path.Combine(_tempDir, $"{outName}-embedded.msi");
        var externalPath = Path.Combine(_tempDir, $"{outName}-external.msi");
        File.WriteAllBytes(embeddedPath, [0xD0, 0xCF, 0x11, 0xE0, 0x01, 0x02]);
        File.WriteAllBytes(externalPath, _externalBytes);

        var model = new BundleModel
        {
            Name = outName,
            Manufacturer = "Contoso",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new() { Id = EmbeddedPackageId, SourcePath = embeddedPath, Type = BundlePackageType.MsiPackage, DisplayName = "Embedded" },
                new() { Id = ExternalPackageId, SourcePath = externalPath, Type = BundlePackageType.MsiPackage, DisplayName = "External", ContainerId = ContainerId }
            }.AsReadOnly(),
            Containers = new List<ContainerModel> { new() { Id = ContainerId, DownloadUrl = ContainerUrl } }.AsReadOnly()
        };

        var outDir = Path.Combine(_tempDir, outName);
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var content = PayloadEmbedder.Extract(result.Value);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!)!;
        var info = Assert.Single(manifest.ExternalContainers);
        var containerPath = Path.Combine(outDir, info.FileName);
        Assert.True(File.Exists(containerPath));
        return (manifest, containerPath);
    }

    /// <summary>
    /// A download seam that faithfully models the verified <c>PayloadDownloader</c>: it serves the file
    /// mapped to the requested URL, but only after confirming the served bytes hash to the expected
    /// SHA-256 — returning a failure Result on any mismatch, exactly as the real downloader does.
    /// </summary>
    private static ExternalContainerAcquirer.DownloadDelegate ServeVerified(string url, string localFile) =>
        (requestedUrl, expectedSha, target, _) =>
        {
            if (!string.Equals(requestedUrl, url, StringComparison.Ordinal) || !File.Exists(localFile))
                return Task.FromResult(Result<string>.Failure(ErrorKind.DownloadError, "not found"));

            using (var stream = File.OpenRead(localFile))
            {
                var actual = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Result<string>.Failure(ErrorKind.DownloadError, "SHA-256 mismatch"));
            }

            File.Copy(localFile, target, overwrite: true);
            return Task.FromResult(Result<string>.Success(target));
        };

    private static Func<InstallerManifest, IReadOnlyList<TocEntry>, Result<Unit>> TrustPassthrough() =>
        (_, _) => Unit.Value;

    [Fact]
    public async Task AcquireAll_VerifiesAndExtracts_PlacesPayloadInCacheDir()
    {
        var (manifest, containerPath) = CompileFixture("AcquireOk");
        var cacheDir = Path.Combine(_tempDir, "cache-ok");
        Directory.CreateDirectory(cacheDir);

        var acquirer = new ExternalContainerAcquirer(ServeVerified(ContainerUrl, containerPath), TrustPassthrough());
        var result = await acquirer.AcquireAllAsync(manifest, cacheDir, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        // The external payload landed at {cacheDir}/{PackageId} — exactly where the resolved-path install
        // chain (#56) looks — and decompressed back to the original bytes.
        var extracted = Path.Combine(cacheDir, ExternalPackageId);
        Assert.True(File.Exists(extracted), $"expected extracted payload at {extracted}");
        Assert.Equal(_externalBytes, await File.ReadAllBytesAsync(extracted));
    }

    [Fact]
    public async Task AcquireAll_ContainerHashMismatch_FailsLoud_AndInstallsNothing()
    {
        var (manifest, _) = CompileFixture("AcquireBadHash");
        var cacheDir = Path.Combine(_tempDir, "cache-badhash");
        Directory.CreateDirectory(cacheDir);

        // Serve a DIFFERENT file at the container URL: its bytes will not hash to the manifest's declared
        // container SHA-256, so the verified download seam rejects it — the container is never opened.
        var rogue = Path.Combine(_tempDir, "rogue.ffcontainer");
        await File.WriteAllBytesAsync(rogue, [0x00, 0x11, 0x22, 0x33, 0x44]);

        var acquirer = new ExternalContainerAcquirer(ServeVerified(ContainerUrl, rogue), TrustPassthrough());
        var result = await acquirer.AcquireAllAsync(manifest, cacheDir, CancellationToken.None);

        Assert.True(result.IsFailure, "a container whose bytes do not match the declared hash must be rejected");
        Assert.False(File.Exists(Path.Combine(cacheDir, ExternalPackageId)),
            "no payload may be installed from an unverified container");
    }

    [Fact]
    public async Task AcquireAll_SignedSetVerificationFails_AbortsBeforeExtraction()
    {
        var (manifest, containerPath) = CompileFixture("AcquireUntrusted");
        var cacheDir = Path.Combine(_tempDir, "cache-untrusted");
        Directory.CreateDirectory(cacheDir);

        // The container downloads and hashes fine, but the signed-set trust gate rejects it. Extraction
        // must NOT run — an untrusted (e.g. re-hosted, attacker-re-signed) container is never installed.
        IReadOnlyList<TocEntry>? seenToc = null;
        var acquirer = new ExternalContainerAcquirer(
            ServeVerified(ContainerUrl, containerPath),
            (_, toc) => { seenToc = toc; return Result<Unit>.Failure(ErrorKind.SecurityError, "untrusted"); });

        var result = await acquirer.AcquireAllAsync(manifest, cacheDir, CancellationToken.None);

        Assert.True(result.IsFailure, "an untrusted container must be rejected");
        Assert.False(File.Exists(Path.Combine(cacheDir, ExternalPackageId)),
            "extraction must not run when signed-set verification fails");

        // The gate is fed the CONTAINER's own TOC (the payloads about to be extracted), so tamper of the
        // container's payloads is what gets bound to the signed set — not the exe's embedded TOC.
        Assert.NotNull(seenToc);
        Assert.Equal(ExternalPackageId, Assert.Single(seenToc!).PackageId);
    }

    [Fact]
    public async Task AcquireAll_ContainerMembershipMismatch_FailsLoud()
    {
        var (manifest, containerPath) = CompileFixture("AcquireMembership");
        var cacheDir = Path.Combine(_tempDir, "cache-membership");
        Directory.CreateDirectory(cacheDir);

        // Rewrite the manifest to claim the container carries a payload it does not — the container's real
        // TOC will not match the declared membership, so acquisition is refused.
        var info = manifest.ExternalContainers[0];
        var tampered = manifest with
        {
            ExternalContainers = [info with { PackageIds = ["GhostPackage"] }]
        };

        var acquirer = new ExternalContainerAcquirer(ServeVerified(ContainerUrl, containerPath), TrustPassthrough());
        var result = await acquirer.AcquireAllAsync(tampered, cacheDir, CancellationToken.None);

        Assert.True(result.IsFailure, "a container whose contents differ from the manifest must be rejected");
        Assert.False(File.Exists(Path.Combine(cacheDir, ExternalPackageId)));
    }
}
