namespace FalkForge.Engine;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Small single-purpose helpers extracted from <c>Program</c> (pure move): the terminal-state →
/// process-exit-code mapping, SBOM-attestation extraction, and the embedded-bundle-footer check.
/// Grouped in one file because each is a few lines and none is a primary type on its own.
/// </summary>
internal static class EngineProgramHelpers
{
    /// <summary>
    /// Maps a terminal engine state to the process exit code the bootstrapper and the
    /// direct-manifest path both return. Single-sourced so the two run paths can never drift.
    /// </summary>
    internal static int ToExitCode(EngineTerminalState state) => state switch
    {
        EngineTerminalState.Completed  => 0,
        EngineTerminalState.Cancelled  => 2,
        EngineTerminalState.RolledBack => 3,
        EngineTerminalState.Failed     => 1,
        _                              => 1
    };

    /// <summary>
    /// Extracts the SBOM attestation from an installer manifest to a file.
    /// Returns 0 on success, 1 if no SBOM is available.
    /// </summary>
    internal static int ExtractSbom(InstallerManifest manifest, string outputPath)
    {
        if (manifest.SbomAttestation is null)
        {
            Console.Error.WriteLine("No SBOM available in this installer.");
            return 1;
        }

        File.WriteAllText(outputPath, manifest.SbomAttestation);
        Console.WriteLine($"SBOM written to {outputPath}");
        return 0;
    }

    /// <summary>
    /// Checks whether the current process executable has an embedded FALKBUNDLE footer.
    /// </summary>
    internal static bool HasEmbeddedBundle()
    {
        var exePath = Environment.ProcessPath;
        return exePath is not null && BundleReader.HasBundleFooter(exePath);
    }
}
