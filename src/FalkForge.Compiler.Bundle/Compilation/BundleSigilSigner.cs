using FalkForge.Models;
using FalkForge.Signing;

namespace FalkForge.Compiler.Bundle.Compilation;

internal sealed class BundleSigilSigner
{
    internal Result<string> RunSignManifest(
        string payloadDir,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var args = new List<string> { "sign-manifest", payloadDir, "--output", outputPath };
        SigilProcessRunner.AppendKeyArgs(args, config);
        return SigilProcessRunner.Run(args, ErrorKind.BundleError);
    }

    /// <summary>
    /// Builds the <c>sigil attest</c> argument list. The <c>--type</c> flag becomes the DSSE
    /// envelope's <c>predicateType</c>, i.e. a claim <b>inside</b> the signed envelope about what
    /// the predicate document is — so it must describe the bytes that were actually written and
    /// nothing else.
    ///
    /// <para>The switch has no catch-all arm on purpose. It used to fold every unrecognised value
    /// into <c>"spdx"</c>, which is how a CycloneDX predicate came to be signed under a
    /// <c>predicateType</c> of SPDX. An unrecognised <see cref="SbomFormat"/> is a programming
    /// error, not user input, and a confident wrong label is worse than a crash. Mirrors
    /// <c>IntegritySigner.ToFormatTag</c> on the MSI side.</para>
    /// </summary>
    internal static List<string> BuildAttestArgs(
        string artifactPath,
        string sbomPath,
        SbomFormat format,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var formatString = format switch
        {
            SbomFormat.Spdx => "spdx",
            SbomFormat.CycloneDx => "cyclonedx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported SBOM format.")
        };

        var args = new List<string>
        {
            "attest",
            artifactPath,
            "--predicate", sbomPath,
            "--type", formatString,
            "--output", outputPath
        };

        SigilProcessRunner.AppendKeyArgs(args, config);
        return args;
    }

    internal Result<string> RunAttest(
        string artifactPath,
        string sbomPath,
        SbomFormat format,
        string outputPath,
        IntegrityConfiguration? config)
        => SigilProcessRunner.Run(
            BuildAttestArgs(artifactPath, sbomPath, format, outputPath, config), ErrorKind.BundleError);
}
