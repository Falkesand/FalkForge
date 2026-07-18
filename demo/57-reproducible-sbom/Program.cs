using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;
using FalkForge.Sbom;

// REPRODUCIBLE BUILDS
// ───────────────────
// "Reproducible" means: build the same source twice → get byte-identical output.
// This matters because it lets anyone independently verify that a shipped installer
// really came from the stated source code. If the bytes match, no tampering occurred.
// The trick: all timestamps in the MSI are pinned to SOURCE_DATE_EPOCH (a Unix timestamp
// you set in the environment), so the compiler writes the same bytes every time.
//
// SBOM (Software Bill of Materials)
// ──────────────────────────────────
// An SBOM is a list of every component inside the installer — files, versions, hashes.
// It is the supply-chain manifest that lets auditors, security scanners, and end users
// know exactly what they are installing. FalkForge emits CycloneDX JSON format.
//
// This demo does not call Integrity() so it can stay focused on reproducibility + SBOM, but
// `PackageBuilder.Integrity(...)` works on MSI too (the same pure-.NET ECDSA P-256 signer as
// `BundleBuilder.Integrity(...)` for EXE bundles — see demo 59), and it composes with
// Reproducible(): because an ECDSA signature is intentionally nondeterministic (fresh random
// nonce every call), combining the two skips the signature's IN-BAND MSI table row — embedding
// it there would make every "reproducible" rebuild produce different bytes — and instead writes
// it sidecar-only (`<msi>.sig.json`) so the MSI artifact itself stays byte-identical across
// builds. `forge verify --rebuild` (demonstrated below) remains the way to prove the artifact's
// bytes match a stated source tree; Integrity() is the separate, independent question of whether
// the artifact carries a verifiable publisher signature at all.

return Installer.Build(args, package =>
{
    package.Name = "Reproducible SBOM Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    package.MediaTemplate(mt =>
    {
        mt.CabinetTemplate("data{0}.cab");
        mt.CompressionLevel(CompressionLevel.High);
        mt.EmbedCabinet(true);
    });

    // Pin all MSI timestamps to SOURCE_DATE_EPOCH.
    // The env var must be set before calling Build(); the CLI --reproducible flag does
    // this automatically, or set it yourself: SOURCE_DATE_EPOCH=1700000000
    package.Reproducible();

    // Emit a CycloneDX SBOM sidecar (.cdx.json) alongside the MSI output.
    // The serial number and timestamp are derived from the build content when
    // SOURCE_DATE_EPOCH is active, so the SBOM is itself reproducible.
    // Pass a configure action to declare additional components (e.g. runtimes you bundle).
    package.Sbom(sbom => sbom
        .AddComponent(
            name: "Microsoft .NET Runtime",
            version: "10.0.0",
            type: SbomComponentType.Library,
            sha256: "0000000000000000000000000000000000000000000000000000000000000000",
            publisher: "Microsoft"));

    package.Files(files => files
        .Add("payload/readme.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "ReproducibleSbom"));
}, new MsiCompiler());
