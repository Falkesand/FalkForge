namespace FalkForge.Engine.Tests.Integrity;

using System.Diagnostics;
using Xunit;

/// <summary>
/// Proves <c>TrustedKeys.targets</c> rejects a malformed classical <c>FalkForgeTrustedKey</c> fingerprint
/// at build time instead of baking it in silently. A real fingerprint is the SHA-256 of a signing key's
/// SubjectPublicKeyInfo: exactly 64 hex characters after normalization. A short or truncated value (a typo,
/// a bad paste, a generator bug) would otherwise pin the engine to a fingerprint no real key can ever match
/// -- every bundle from that publisher then fails verification, discovered only at the customer's install,
/// not on the build box. Measured 2026-08-25: before this fix, <c>-p:FalkForgeTrustedKey=ABCD1234</c> (8
/// hex chars) built clean and baked "ABCD1234" into <c>BakedTrustedKeys.Fingerprints</c> unchanged.
/// <para>
/// The targets file's own <c>PqFingerprint</c> metadata check (FALKPQ002) already enforces this rule for
/// the PQ companion fingerprint; this test proves the classical fingerprint now gets the same treatment.
/// </para>
/// <para>
/// Runs `dotnet build` against a throwaway SDK-style project that imports the real
/// <c>TrustedKeys.targets</c> and invokes only the <c>_GenerateFalkTrustedKeys</c> target directly (not a
/// full compile or NativeAOT publish) -- that target is where the validation lives, and skipping the rest
/// keeps the test fast and free of unrelated native-toolchain dependencies. <see cref="BakedTrustedKeysTests"/>
/// already documents why a full rebuild-in-test is avoided for the happy path; this test needs the real
/// MSBuild task to prove the error path, so it pays that cost once, scoped to the one target.
/// </para>
/// </summary>
public sealed class TrustedKeysTargetsValidationTests : IDisposable
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);
    private static readonly string TargetsFilePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FalkForge.Engine", "TrustedKeys.targets"));

    private readonly string _projectDir = Path.Combine(
        Path.GetTempPath(), $"falk-trustedkeys-targets-{Guid.NewGuid():N}");

    public TrustedKeysTargetsValidationTests()
    {
        Directory.CreateDirectory(_projectDir);
        File.WriteAllText(Path.Combine(_projectDir, "Scratch.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Import Project="{TargetsFilePath}" />
            </Project>
            """);
    }

    [Fact]
    public void ShortClassicalFingerprint_FailsBuildWithDiagnosticCode()
    {
        var result = RunGenerateTarget("-p:FalkForgeTrustedKey=ABCD1234");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("FALKPQ004", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(GeneratedFilePath()));
    }

    [Fact]
    public void ValidSixtyFourCharFingerprint_BuildSucceedsAndBakesItIn()
    {
        const string validFingerprint =
            "6C0C66E36EFE54BF5796C2D5DE2D9A402CAF8B2CFAF590769BA46DE784A98AE1";

        var result = RunGenerateTarget($"-p:FalkForgeTrustedKey={validFingerprint}");

        Assert.Equal(0, result.ExitCode);
        var generated = File.ReadAllText(GeneratedFilePath());
        Assert.Contains(validFingerprint, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFingerprintSupplied_BuildSucceedsWithEmptySet()
    {
        var result = RunGenerateTarget(extraArgs: null);

        Assert.Equal(0, result.ExitCode);
        var generated = File.ReadAllText(GeneratedFilePath());
        Assert.Contains("System.Collections.Frozen.FrozenSet.ToFrozenSet(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"", generated, StringComparison.Ordinal);
    }

    private string GeneratedFilePath() =>
        Path.Combine(_projectDir, "obj", "Debug", "net10.0", "TrustedKeys.g.cs");

    private (int ExitCode, string Output) RunGenerateTarget(string? extraArgs)
    {
        var arguments = $"build \"{Path.Combine(_projectDir, "Scratch.csproj")}\" " +
                         "-t:_GenerateFalkTrustedKeys -nodeReuse:false -v:minimal";
        if (extraArgs is not null)
            arguments += " " + extraArgs;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _projectDir,
        };
        // Isolate this scratch build's MSBuild node from the shared daemon pool.
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)BuildTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            throw new TimeoutException(
                $"Scratch build timed out after {BuildTimeout.TotalSeconds}s.");
        }

        stdout.Wait();
        stderr.Wait();

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    public void Dispose() => TestTemp.TryDelete(_projectDir);
}
