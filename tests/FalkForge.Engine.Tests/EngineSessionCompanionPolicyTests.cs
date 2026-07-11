namespace FalkForge.Engine.Tests;

using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// The elevation companion executes ELEVATED (SYSTEM for per-machine installs), so WHERE the
/// session finds it is a security decision, not a convenience. These tests pin the
/// <see cref="ElevationCompanionPolicy"/> contract on <see cref="EngineSession.BindToPipe"/>:
///
/// <list type="bullet">
///   <item><description><b>Bundle bootstrap, manifest declares no companion
///   (<see cref="ElevationCompanionPolicy.NoneDeclared"/>):</b> the manifest is authoritative.
///   A <c>FalkForge.Engine.Elevation.exe</c> planted beside the engine (the classic
///   binary-planting attack against a signed bundle authored
///   <c>WithoutElevationCompanion()</c>) must NEVER be wired — the session runs per-user with
///   no elevation gateway.</description></item>
///   <item><description><b>Bundle bootstrap, manifest declares a verified companion
///   (<see cref="ElevationCompanionPolicy.VerifiedPath"/>):</b> only the integrity-verified
///   extracted path is wired; if it is gone the session degrades to per-user rather than
///   falling back to the (unverified) ambient probe.</description></item>
///   <item><description><b>Plain engine run
///   (<see cref="ElevationCompanionPolicy.AmbientAllowed"/>, the default):</b> the companion
///   legitimately ships beside the engine, so the ambient probe stays the intended
///   mechanism.</description></item>
/// </list>
///
/// All tests live in one class so xunit serializes them: they share the planted companion file
/// in <see cref="AppContext.BaseDirectory"/>.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EngineSessionCompanionPolicyTests : IDisposable
{
    private const string CompanionFileName = "FalkForge.Engine.Elevation.exe";

    private readonly string _tempDir;
    private readonly string _plantedCompanionPath;
    private readonly bool _plantedByThisTest;

    public EngineSessionCompanionPolicyTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_CompanionPolicy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Plant a fake companion beside the engine (= the test host's base directory). This is
        // exactly the attacker move: drop FalkForge.Engine.Elevation.exe next to the bundle exe.
        // Guard: never clobber a real companion if one is ever shipped into the test bin dir.
        _plantedCompanionPath = Path.Combine(AppContext.BaseDirectory, CompanionFileName);
        if (!File.Exists(_plantedCompanionPath))
        {
            File.WriteAllBytes(_plantedCompanionPath, [(byte)'M', (byte)'Z', 0x00]);
            _plantedByThisTest = true;
        }
    }

    public void Dispose()
    {
        if (_plantedByThisTest)
        {
            try { File.Delete(_plantedCompanionPath); } catch (IOException) { /* best effort */ }
        }

        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string WriteManifest()
    {
        var manifest = new InstallerManifest
        {
            Name = "CompanionPolicy",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(), // fresh per manifest: the per-bundle instance lock must not collide
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = []
        };

        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest));
        return manifestPath;
    }

    private EngineSessionOptions Options(
        ElevationCompanionPolicy policy, string? verifiedPath = null) => new()
    {
        ElevationCompanionPolicy = policy,
        ElevationCompanionPath = verifiedPath,
        LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
        WriteJournal = false
    };

    [Fact]
    public async Task NoneDeclared_PlantedCompanionBesideEngine_IsNeverWired_PerUser()
    {
        // A signed bundle authored WithoutElevationCompanion() declares no companion; the
        // bootstrapper passes NoneDeclared. The planted binary beside the engine must not be
        // launched elevated — no elevation gateway, per-user session.
        Assert.True(File.Exists(_plantedCompanionPath), "test setup: planted companion must exist");

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(), Options(ElevationCompanionPolicy.NoneDeclared));

        Assert.Null(session.ElevationGateway);
    }

    [Fact]
    public async Task VerifiedPath_MissingVerifiedFile_DoesNotFallBackToAmbientProbe()
    {
        // The manifest declared a companion and the resolver verified it, but the extracted file
        // is gone by bind time. Fail-safe: degrade to per-user; never substitute the unverified
        // planted binary from the ambient probe.
        Assert.True(File.Exists(_plantedCompanionPath), "test setup: planted companion must exist");
        var vanishedPath = Path.Combine(_tempDir, "vanished-companion.exe");

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.VerifiedPath, verifiedPath: vanishedPath));

        Assert.Null(session.ElevationGateway);
    }

    [Fact]
    public async Task VerifiedPath_ExistingVerifiedCompanion_IsWired()
    {
        var verifiedPath = Path.Combine(_tempDir, "verified-companion.exe");
        File.WriteAllBytes(verifiedPath, [(byte)'M', (byte)'Z', 0x01]);

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.VerifiedPath, verifiedPath: verifiedPath));

        Assert.NotNull(session.ElevationGateway);
    }

    [Fact]
    public async Task AmbientAllowed_Default_CompanionBesideEngine_IsStillWired()
    {
        // The legitimate non-bundle scenario: a UI-driven install where the companion ships
        // beside the engine. The ambient probe is the normal, intended mechanism and must stay.
        Assert.True(File.Exists(_plantedCompanionPath), "test setup: companion beside engine must exist");

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(), Options(ElevationCompanionPolicy.AmbientAllowed));

        Assert.NotNull(session.ElevationGateway);
    }
}
