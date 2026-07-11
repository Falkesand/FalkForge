using System.Diagnostics;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Proves the runnable-bundle story end to end: a DEFAULT bundle build (no explicit stub, no
/// placeholder opt-in) embeds the REAL published NativeAOT engine as its PE front, and the
/// produced exe genuinely self-extracts — it launches the actual engine binary, which reads its
/// own embedded TOC and writes the payload back out byte-for-byte. This is the property a beta
/// tester depends on: a bundle that only verifies but cannot run is useless.
/// <para>
/// The tests are gated on the NativeAOT engine published by <c>scripts/publish.ps1</c> to
/// <c>artifacts/publish/engine</c>. A full INSTALL e2e is deliberately not included: executing
/// the install path mutates machine state (MSI registration, elevation) and cannot run
/// deterministically inside a unit-test host — the extraction path exercised here is the same
/// self-reading pipeline (BundleReader over Environment.ProcessPath) the installer flow starts
/// with.
/// </para>
/// </summary>
public sealed class RunnableBundleEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public RunnableBundleEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RunnableBundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static string? FindPublishedEngine()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;
        var candidate = Path.Combine(root, "artifacts", "publish", "engine", "FalkForge.Engine.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{Path.GetFileName(fileName)} did not exit within 2 minutes. stdout: {stdout} stderr: {stderr}");
        }

        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public void DefaultBundleBuild_EmbedsRealEngine_AndSelfExtracts()
    {
        var enginePath = FindPublishedEngine();
        Assert.SkipUnless(enginePath is not null,
            "Published NativeAOT engine not found at artifacts/publish/engine — run scripts/publish.ps1 first. " +
            "This gate exists because the runnable-bundle e2e requires the multi-minute NativeAOT publish.");

        // Payload with distinctive bytes so byte-for-byte extraction can be asserted.
        var payloadPath = Path.Combine(_tempDir, "app-payload.msi");
        var payloadBytes = new byte[64 * 1024];
        Random.Shared.NextBytes(payloadBytes);
        File.WriteAllBytes(payloadPath, payloadBytes);

        var model = new BundleModel
        {
            Name = "RunnableE2E",
            Manufacturer = "Contoso",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "MainMsi",
                    SourcePath = payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Main package"
                }
            }.AsReadOnly()
        };

        // DEFAULT path: no EngineStubPath, no AllowPlaceholderStub — resolution must find the
        // published engine via the repository walk-up probe.
        var compileResult = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "out"));
        Assert.True(compileResult.IsSuccess,
            compileResult.IsFailure ? compileResult.Error.Message : null);
        var bundlePath = compileResult.Value;

        // The bundle's front is the real engine: PE header present and at least the engine's size.
        using (var stream = File.OpenRead(bundlePath))
        {
            var prefix = new byte[2];
            stream.ReadExactly(prefix);
            Assert.Equal((byte)'M', prefix[0]);
            Assert.Equal((byte)'Z', prefix[1]);
            Assert.True(stream.Length > new FileInfo(enginePath!).Length,
                "bundle must be strictly larger than the engine it embeds");
        }

        // --extract-list: the running stub reads its OWN embedded TOC.
        var (listExit, listOut, listErr) = RunProcess(bundlePath, "--extract-list");
        Assert.True(listExit == 0, $"--extract-list failed (exit {listExit}). stderr: {listErr}");
        Assert.Contains("MainMsi", listOut, StringComparison.Ordinal);

        // --extract: payload comes back byte-for-byte.
        var extractDir = Path.Combine(_tempDir, "extracted");
        var (extractExit, _, extractErr) = RunProcess(bundlePath, "--extract", extractDir);
        Assert.True(extractExit == 0, $"--extract failed (exit {extractExit}). stderr: {extractErr}");

        var extractedFile = Path.Combine(extractDir, "MainMsi", "MainMsi.dat");
        Assert.True(File.Exists(extractedFile), $"expected extracted payload at {extractedFile}");
        Assert.Equal(payloadBytes, File.ReadAllBytes(extractedFile));
    }

    [Fact]
    public void DefaultBundleBuild_CarriesRealElevationCompanion_VerifiedByteForByte()
    {
        // A per-machine elevated install from a LONE distributed bundle exe requires the elevation
        // companion to travel inside the bundle. This proves the default build embeds the REAL
        // published FalkForge.Engine.Elevation.exe (resolved beside the published engine), that
        // the running engine lists it, and that the extracted companion is byte-for-byte the
        // published binary after its hash verification.
        //
        // A full per-machine INSTALL e2e is deliberately not included for the same reason as
        // above: launching the elevated companion requires a UAC prompt and administrator rights
        // and mutates machine state, which cannot run deterministically inside a unit-test host.
        // The extract+verify path exercised here is the exact trust pipeline the bootstrapper
        // wires before spawning the companion.
        var enginePath = FindPublishedEngine();
        Assert.SkipUnless(enginePath is not null,
            "Published NativeAOT engine not found at artifacts/publish/engine — run scripts/publish.ps1 first. " +
            "This gate exists because the runnable-bundle e2e requires the multi-minute NativeAOT publish.");

        var companionPath = Path.Combine(
            Path.GetDirectoryName(enginePath!)!, "FalkForge.Engine.Elevation.exe");
        Assert.SkipUnless(File.Exists(companionPath),
            "Published NativeAOT elevation companion not found beside the published engine — run " +
            "scripts/publish.ps1 first (it publishes FalkForge.Engine.exe and FalkForge.Engine.Elevation.exe together).");

        var payloadPath = Path.Combine(_tempDir, "app-payload.msi");
        File.WriteAllBytes(payloadPath, new byte[16 * 1024]);

        var model = new BundleModel
        {
            Name = "CompanionE2E",
            Manufacturer = "Contoso",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "MainMsi",
                    SourcePath = payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Main package"
                }
            }.AsReadOnly()
        };

        var compileResult = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "out-companion"));
        Assert.True(compileResult.IsSuccess,
            compileResult.IsFailure ? compileResult.Error.Message : null);
        var bundlePath = compileResult.Value;

        // The running engine reads its OWN TOC and lists the companion payload.
        var (listExit, listOut, listErr) = RunProcess(bundlePath, "--extract-list");
        Assert.True(listExit == 0, $"--extract-list failed (exit {listExit}). stderr: {listErr}");
        Assert.Contains("FalkForge.Engine.Elevation.exe", listOut, StringComparison.Ordinal);

        // Extraction streams + SHA-256-verifies; the companion that lands is the published binary.
        var extractDir = Path.Combine(_tempDir, "extracted-companion");
        var (extractExit, _, extractErr) = RunProcess(bundlePath, "--extract", extractDir);
        Assert.True(extractExit == 0, $"--extract failed (exit {extractExit}). stderr: {extractErr}");

        var extractedCompanion = Path.Combine(
            extractDir, "FalkForge.Engine.Elevation.exe", "FalkForge.Engine.Elevation.exe.dat");
        Assert.True(File.Exists(extractedCompanion),
            $"expected extracted companion at {extractedCompanion}");
        Assert.Equal(File.ReadAllBytes(companionPath), File.ReadAllBytes(extractedCompanion));
    }
}
