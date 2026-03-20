namespace FalkForge.Compiler.Msi.Signing;

using FalkForge.Models;
using FalkForge.Signing;

internal sealed class SigilSigner
{
    internal static List<string> BuildSignManifestArgs(string payloadDir, IntegrityConfiguration? config)
    {
        var args = new List<string> { "sign-manifest", payloadDir, "--output", $"{payloadDir}.sig.json" };
        SigilProcessRunner.AppendKeyArgs(args, config);
        return args;
    }

    internal static List<string> BuildAttestArgs(
        string artifactPath,
        string predicatePath,
        SbomFormat format,
        IntegrityConfiguration? config)
    {
        var formatString = format switch
        {
            SbomFormat.Spdx => "spdx",
            SbomFormat.CycloneDx => "cyclonedx",
            _ => "spdx"
        };

        var args = new List<string>
        {
            "attest",
            artifactPath,
            "--predicate", predicatePath,
            "--type", formatString,
            "--output", $"{artifactPath}.attest.json"
        };

        SigilProcessRunner.AppendKeyArgs(args, config);
        return args;
    }

    internal Result<string> RunSignManifest(
        string payloadDir,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var args = BuildSignManifestArgs(payloadDir, config);

        // Override the default output path if caller specified one.
        var outputIndex = args.IndexOf("--output");
        if (outputIndex >= 0 && outputIndex + 1 < args.Count)
            args[outputIndex + 1] = outputPath;

        return SigilProcessRunner.Run(args, ErrorKind.CompilationError);
    }

    internal Result<string> RunAttest(
        string artifactPath,
        string sbomPath,
        SbomFormat format,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var args = BuildAttestArgs(artifactPath, sbomPath, format, config);

        // Override the default output path if caller specified one.
        var outputIndex = args.IndexOf("--output");
        if (outputIndex >= 0 && outputIndex + 1 < args.Count)
            args[outputIndex + 1] = outputPath;

        return SigilProcessRunner.Run(args, ErrorKind.CompilationError);
    }
}
