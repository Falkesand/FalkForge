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

    internal Result<string> RunAttest(
        string artifactPath,
        string sbomPath,
        SbomFormat format,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var formatString = format switch
        {
            SbomFormat.CycloneDx => "cyclonedx",
            _ => "spdx"
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
        return SigilProcessRunner.Run(args, ErrorKind.BundleError);
    }
}
