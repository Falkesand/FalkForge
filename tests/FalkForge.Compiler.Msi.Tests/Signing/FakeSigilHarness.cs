using FalkForge.Signing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Signing;

/// <summary>
/// Puts the FakeSigil test double on PATH so <c>IntegritySigner</c>'s opportunistic SBOM attestation
/// step actually runs. Without a reachable <c>sigil</c> the step returns early by design and there is
/// no <c>SbomAttestation</c> row to assert on at all.
///
/// <para>Shared rather than copied: <c>MsiIntegritySigningTests</c> and
/// <c>IntegrityAttestationSbomToctouTests</c> each carried a verbatim copy, including the env-var
/// name and the four-level directory walk. Those are exactly the details that rot in one copy only —
/// a renamed variable or a moved output path would leave one suite silently exercising the
/// attestation-absent path while still claiming to test the attestation.</para>
///
/// <para>Every caller must belong to the <c>SigilProcess</c> xUnit collection and restore <c>PATH</c>
/// plus <c>FAKESIGIL_ATTEST_SUCCEEDS</c> in its own <c>Dispose</c>: this mutates process-wide state
/// and the <see cref="SigilDetector"/> cache.</para>
/// </summary>
internal static class FakeSigilHarness
{
    /// <summary>
    /// Resolves the FakeSigil project's own build output directory (where its <c>sigil.exe</c>
    /// apphost lands), deriving the Configuration/TargetFramework segments from the calling test
    /// assembly's <see cref="AppContext.BaseDirectory"/> rather than hardcoding "Debug"/"net10.0" —
    /// robust to a Release run or a future TFM bump. FakeSigil's ProjectReference uses
    /// <c>ReferenceOutputAssembly="false"</c> precisely so its output is NOT copied next to the test
    /// host (see that csproj's comment for why), so tests that want it reachable must explicitly
    /// prepend this directory to PATH themselves.
    /// </summary>
    internal static string ResolveDirectory()
    {
        var binDir = new DirectoryInfo(AppContext.BaseDirectory);       // .../bin/<Config>/<TFM>
        var configDir = binDir.Parent ?? throw new DirectoryNotFoundException();
        var projectDir = configDir.Parent?.Parent ?? throw new DirectoryNotFoundException(); // .../<ThisProject>
        var testsRoot = projectDir.Parent ?? throw new DirectoryNotFoundException();          // .../tests
        return Path.Combine(
            testsRoot.FullName, "FalkForge.Compiler.Msi.Tests.FakeSigil", "bin", configDir.Name, binDir.Name);
    }

    /// <summary>
    /// Prepends the double to <paramref name="originalPath"/> in its opt-in attest-succeeds mode, so
    /// an attestation row is actually produced. The double's default everything-below-<c>--version</c>-fails
    /// behaviour — and the never-fatal-SBOM contract tests that depend on it — is untouched, because
    /// the success mode is env-var gated.
    /// </summary>
    internal static void EnableAttesting(string? originalPath)
    {
        Environment.SetEnvironmentVariable("FAKESIGIL_ATTEST_SUCCEEDS", "1");
        EnableDetection(originalPath);
    }

    /// <summary>
    /// Prepends the double to <paramref name="originalPath"/> in its default mode: <c>--version</c>
    /// succeeds, every other subcommand fails — a real but unconfigured sigil install. Returns the
    /// resolved directory.
    /// </summary>
    internal static string EnableDetection(string? originalPath)
    {
        var directory = ResolveDirectory();
        Assert.True(File.Exists(Path.Combine(directory, "sigil.exe")),
            $"Test setup invariant: FakeSigil build output not found at '{directory}'.");

        Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);
        SigilDetector.Reset();
        Assert.True(SigilDetector.IsAvailable(), "Test setup invariant: the fake sigil.exe must answer --version.");
        return directory;
    }
}
