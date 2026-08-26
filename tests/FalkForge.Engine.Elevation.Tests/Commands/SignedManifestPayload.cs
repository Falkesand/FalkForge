namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Collections.Frozen;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Builds the MsiInstall wire payload the elevated companion now requires — file path, additional args, the
/// (ignored) caller-asserted hash, the package id, and a full installer manifest carrying a publisher-signed
/// integrity envelope — together with the trusted-key set that matches the signing key. This lets the
/// command's require-signed publisher gate be exercised in-process without a baked build.
/// </summary>
internal static class SignedManifestPayload
{
    internal static readonly IReadOnlyDictionary<string, TrustRole> NoRoles =
        FrozenDictionary<string, TrustRole>.Empty;

    internal static readonly IReadOnlyDictionary<string, string> NoPqCompanions =
        FrozenDictionary<string, string>.Empty;

    internal static string Fingerprint(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    internal static IReadOnlySet<string> TrustedSet(ECDsa key) =>
        new HashSet<string>(new[] { Fingerprint(key) }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A manifest with one installable MSI package (<paramref name="packageId"/>) whose signed and declared
    /// hash are both <paramref name="signedHash"/>, signed by <paramref name="signingKey"/>.
    /// </summary>
    internal static string ManifestJson(string packageId, string signedHash, ECDsa signingKey)
        => ManifestJson(
            envelopeEntries: [(packageId, signedHash)],
            packages: [(packageId, signedHash)],
            preUI: [],
            companionSha256: null,
            signingKey: signingKey);

    /// <summary>
    /// Full control over the signed entries and the (possibly divergent) declared manifest packages, so a
    /// test can build a tampered or structurally-odd manifest. A null <paramref name="signingKey"/> yields an
    /// unsigned manifest (no envelope).
    /// </summary>
    internal static string ManifestJson(
        (string id, string sha256)[] envelopeEntries,
        (string id, string sha256)[] packages,
        PreUIPackageInfo[] preUI,
        string? companionSha256,
        ECDsa? signingKey,
        string? uiSha256 = null)
    {
        string? signature = null;
        if (signingKey is not null)
        {
            var files = envelopeEntries
                .Select(e => new ManifestFileEntry { Name = e.id, Sha256 = e.sha256 })
                .ToList();
            signature = IntegrityEnvelopeCodec.Serialize(IntegrityEnvelopeCodec.Sign(files, signingKey));
        }

        var manifest = new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages
                .Select(p => new PackageInfo
                {
                    Id = p.id,
                    Type = PackageType.MsiPackage,
                    DisplayName = p.id,
                    SourcePath = $"C:/cache/{p.id}.msi",
                    Sha256Hash = p.sha256
                })
                .ToArray(),
            PreUIPackages = preUI,
            EngineCompanionSha256 = companionSha256,
            EngineUiSha256 = uiSha256,
            ManifestSignature = signature
        };

        return JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);
    }

    /// <summary>
    /// A manifest that declares one installable MSI package plus one or more signed MSI transforms.
    /// <paramref name="declaredTransforms"/> lists (owning package id, transform id, transform hash): each
    /// becomes a signed envelope file entry and a <see cref="PackageTransformInfo"/> under its owning
    /// package, so the integrity gate binds it (Direction 1) without the transform being installable.
    /// <paramref name="associations"/> is the SIGNED package-to-transform allow-list the companion checks.
    /// Full control lets a test build a cross-package or unassociated case.
    /// </summary>
    internal static string ManifestJson(
        (string id, string sha256)[] packages,
        (string owningPackageId, string transformId, string transformSha256)[] declaredTransforms,
        (string packageId, string[] transformIds)[] associations,
        ECDsa signingKey)
    {
        var files = new List<ManifestFileEntry>();
        foreach (var (id, sha256) in packages)
            files.Add(new ManifestFileEntry { Name = id, Sha256 = sha256 });
        foreach (var (_, transformId, transformSha256) in declaredTransforms)
            files.Add(new ManifestFileEntry { Name = transformId, Sha256 = transformSha256 });

        var associationList = associations
            .Select(a => new PackageTransformAssociation { PackageId = a.packageId, TransformIds = a.transformIds })
            .ToArray();

        var signature = IntegrityEnvelopeCodec.Serialize(
            IntegrityEnvelopeCodec.Sign(
                files, [signingKey], epoch: 0, revoked: [],
                externalContainers: null, transformAssociations: associationList));

        var packageInfos = packages
            .Select(p => new PackageInfo
            {
                Id = p.id,
                Type = PackageType.MsiPackage,
                DisplayName = p.id,
                SourcePath = $"C:/cache/{p.id}.msi",
                Sha256Hash = p.sha256,
                Transforms = declaredTransforms
                    .Where(t => t.owningPackageId == p.id)
                    .Select(t => new PackageTransformInfo { Id = t.transformId, Sha256Hash = t.transformSha256 })
                    .ToArray()
            })
            .ToArray();

        var manifest = new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packageInfos,
            PreUIPackages = [],
            EngineCompanionSha256 = null,
            ManifestSignature = signature
        };

        return JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);
    }

    /// <summary>
    /// A manifest with one package (<paramref name="packageId"/>) whose signed and declared hash are both
    /// <paramref name="signedHash"/>, signed by <paramref name="keys"/> at <paramref name="epoch"/> with
    /// <paramref name="revoked"/>. Used by the elevated trust-store advance tests, where the epoch and
    /// revocations must be cryptographically covered so the companion can take them from the verified
    /// envelope. Multiple keys support the release+recovery quorum a genuine epoch advance (KeyChange) needs.
    /// </summary>
    internal static string AdvanceManifestJson(
        string packageId, string signedHash, int epoch, string[] revoked, params ECDsa[] keys)
    {
        var files = new List<ManifestFileEntry> { new() { Name = packageId, Sha256 = signedHash } };
        var signature = IntegrityEnvelopeCodec.Serialize(
            IntegrityEnvelopeCodec.Sign(files, keys, epoch, revoked));

        var manifest = new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new PackageInfo
                {
                    Id = packageId,
                    Type = PackageType.MsiPackage,
                    DisplayName = packageId,
                    SourcePath = $"C:/cache/{packageId}.msi",
                    Sha256Hash = signedHash
                }
            ],
            PreUIPackages = [],
            EngineCompanionSha256 = null,
            ManifestSignature = signature
        };

        return JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);
    }

    internal static IReadOnlySet<string> TrustedSet(params ECDsa[] keys) =>
        new HashSet<string>(keys.Select(Fingerprint), StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, TrustRole> Roles(params (ECDsa key, TrustRole role)[] entries) =>
        entries.ToDictionary(e => Fingerprint(e.key), e => e.role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sentinel prefixing the versioned MsiUninstall wire. Mirrors
    /// <c>MsiUninstallCommand.WireFormatMagic</c> / <c>MsiExecutor.UninstallWireFormatMagic</c>.
    /// </summary>
    internal const int UninstallWireMagic = 0x4655_4E31;

    /// <summary>
    /// A manifest for an uninstall: one dummy installable MSI package (so the integrity gate's package
    /// coverage check passes) and a signed flat allow-set of <paramref name="authorizedProductCodes"/>.
    /// The allow-set is what the uninstall companion checks the requested product code against.
    /// </summary>
    internal static string UninstallManifestJson(string[] authorizedProductCodes, ECDsa signingKey)
    {
        const string pkgId = "App.Main";
        var hash = new string('A', 64);
        var files = new List<ManifestFileEntry> { new() { Name = pkgId, Sha256 = hash } };

        var signature = IntegrityEnvelopeCodec.Serialize(
            IntegrityEnvelopeCodec.Sign(
                files, [signingKey], epoch: 0, revoked: [],
                externalContainers: null, transformAssociations: null, productCodes: authorizedProductCodes));

        var manifest = new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new PackageInfo
                {
                    Id = pkgId,
                    Type = PackageType.MsiPackage,
                    DisplayName = pkgId,
                    SourcePath = $"C:/cache/{pkgId}.msi",
                    Sha256Hash = hash
                }
            ],
            PreUIPackages = [],
            EngineCompanionSha256 = null,
            ManifestSignature = signature
        };

        return JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);
    }

    /// <summary>
    /// An uninstall manifest whose signed product-code allow-set has been tampered after signing: signed for
    /// <paramref name="signedCodes"/>, then the envelope's product-code set is overwritten with
    /// <paramref name="tamperedCodes"/> and re-serialized, so the signature no longer covers the set the
    /// companion reads. The gate must reject it (INT001).
    /// </summary>
    internal static string TamperedUninstallManifestJson(
        string[] signedCodes, string[] tamperedCodes, ECDsa signingKey)
    {
        const string pkgId = "App.Main";
        var hash = new string('A', 64);
        var files = new List<ManifestFileEntry> { new() { Name = pkgId, Sha256 = hash } };

        var envelope = IntegrityEnvelopeCodec.Sign(
            files, [signingKey], epoch: 0, revoked: [],
            externalContainers: null, transformAssociations: null, productCodes: signedCodes);
        envelope.ProductCodes = tamperedCodes;
        var signature = IntegrityEnvelopeCodec.Serialize(envelope);

        var manifest = new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new PackageInfo
                {
                    Id = pkgId,
                    Type = PackageType.MsiPackage,
                    DisplayName = pkgId,
                    SourcePath = $"C:/cache/{pkgId}.msi",
                    Sha256Hash = hash
                }
            ],
            PreUIPackages = [],
            EngineCompanionSha256 = null,
            ManifestSignature = signature
        };

        return JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);
    }

    /// <summary>An unsigned uninstall manifest (one package, no envelope) — refused on the require-signed path.</summary>
    internal static string UnsignedUninstallManifestJson()
        => ManifestJson(
            envelopeEntries: [], packages: [("App.Main", new string('A', 64))],
            preUI: [], companionSha256: null, signingKey: null);

    /// <summary>Builds the versioned MsiUninstall wire payload: magic sentinel, product code, signed manifest.</summary>
    internal static byte[] BuildUninstall(string productCode, string manifestJson)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(UninstallWireMagic);
        writer.Write(productCode);
        writer.Write(manifestJson);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Builds the pre-change bare-product-code uninstall payload the companion must now refuse.</summary>
    internal static byte[] BuildOldFormatUninstall(string productCode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(productCode);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Builds the full MsiInstall wire payload the companion parses.</summary>
    internal static byte[] Build(
        string msiPath,
        string additionalArgs,
        string packageId,
        string manifestJson,
        (string name, byte[] value)[]? secrets = null,
        (string id, string path)[]? transforms = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Write(string.Empty); // caller-asserted expected hash — read for wire compat, ignored for trust
        writer.Write(packageId);
        writer.Write(manifestJson);

        // The per-package transform block is always present (count 0 when none), and sits before the
        // optional secret block so the secret block stays detectable by stream position.
        writer.Write(transforms?.Length ?? 0);
        foreach (var (id, path) in transforms ?? [])
        {
            writer.Write(id);
            writer.Write(path);
        }

        if (secrets is { Length: > 0 })
        {
            writer.Write(secrets.Length);
            foreach (var (name, value) in secrets)
            {
                writer.Write(name);
                writer.Write(value.Length);
                writer.Write(value);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }
}
