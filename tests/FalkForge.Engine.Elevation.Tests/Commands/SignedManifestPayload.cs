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
        ECDsa? signingKey)
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
            ManifestSignature = signature
        };

        return JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);
    }

    /// <summary>
    /// A manifest that declares one installable MSI package plus one or more signed MSI transforms (D36).
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

        // The per-package transform block (D36) is always present (count 0 when none), and sits before the
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
