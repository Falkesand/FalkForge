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

    /// <summary>Builds the full MsiInstall wire payload the companion parses.</summary>
    internal static byte[] Build(
        string msiPath,
        string additionalArgs,
        string packageId,
        string manifestJson,
        (string name, byte[] value)[]? secrets = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Write(string.Empty); // caller-asserted expected hash — read for wire compat, ignored for trust
        writer.Write(packageId);
        writer.Write(manifestJson);
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
