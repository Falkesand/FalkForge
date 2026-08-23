namespace FalkForge.Engine.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Integrity;
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

        TestTemp.TryDelete(_tempDir);
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
        ElevationCompanionPolicy policy, string? verifiedPath = null, string? verifiedHash = null) => new()
    {
        ElevationCompanionPolicy = policy,
        ElevationCompanionPath = verifiedPath,
        ElevationCompanionSha256 = verifiedHash,
        LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
        WriteJournal = false
    };

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Writes a companion file and returns its path together with the digest of what was written,
    /// which is what the bootstrapper hands the session.
    /// </summary>
    private static (string Path, string Sha256) WriteCompanion(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        return (path, Sha256Hex(bytes));
    }

    /// <summary>
    /// Spells a path the way Windows does when asked what an open handle refers to: every
    /// junction followed and every short (8.3) component expanded. <see cref="Path.GetTempPath"/>
    /// can hand back a short component, so without this the assertions would be comparing two
    /// spellings of one file.
    /// </summary>
    private static string ResolveFinalPath(string path)
    {
        using var handle = File.OpenHandle(path);
        return HashBoundFile.TryGetFinalPath(handle)
            ?? throw new InvalidOperationException($"Windows could not name the file at '{path}'.");
    }

    /// <summary>
    /// The path the wired gateway will hand its process launcher.
    /// <para>
    /// This is the closest a test in this project can get to the launch itself, and it is one rung
    /// below what the payload-crossing tests manage. Those pass a fake process runner in and assert
    /// on the bytes the consumer actually read. Here the gateway is built with a hardcoded
    /// <c>new ProcessLauncher()</c>, so nothing can observe what reaches <c>Process.Start</c>.
    /// Reading the file back through this exact string, after the attack, is the substitute.
    /// </para>
    /// </summary>
    private static string LaunchPathOf(EngineSession session)
    {
        var gateway = Assert.IsType<NamedPipeElevationGateway>(session.ElevationGateway);
        return gateway.CompanionExePath;
    }

    // Arranging bytes on disk happens through these rather than inline in the async test bodies.
    // The analyzer set flags a synchronous file call inside an async method, and there is nothing
    // asynchronous about writing three bytes into a temp directory.
    private static void WriteBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    private static byte[] ReadBytes(string path) => File.ReadAllBytes(path);

    private static void AssertCannotBeWrittenOrDeleted(string path)
    {
        Assert.Throws<IOException>(() => File.WriteAllBytes(path, "attacker"u8.ToArray()));
        Assert.Throws<IOException>(() => File.Delete(path));
    }

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
        var hash = Sha256Hex([(byte)'M', (byte)'Z', 0x01]);

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.VerifiedPath, vanishedPath, hash));

        Assert.Null(session.ElevationGateway);
    }

    [Fact]
    public async Task VerifiedPath_ExistingVerifiedCompanion_IsWired()
    {
        var (verifiedPath, hash) = WriteCompanion(
            Path.Combine(_tempDir, "verified-companion.exe"), [(byte)'M', (byte)'Z', 0x01]);

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.VerifiedPath, verifiedPath, hash));

        Assert.NotNull(session.ElevationGateway);
        Assert.Equal(ResolveFinalPath(verifiedPath), LaunchPathOf(session));
    }

    // ── The launch-site binding ─────────────────────────────────────────────────────────────
    // The bootstrapper proves the companion's bytes while it unpacks the bundle. The session does
    // not start the process until the pre-UI bootstrap has run, the UI process has started, and
    // the user has read a licence, chosen a directory and clicked Install. The extraction
    // directory lives under %TEMP% and belongs to the user, so a process running as that user owns
    // it for that whole wait. These tests use the wait.

    [Fact]
    public async Task VerifiedPath_BytesReplacedAfterVerification_IsNotWired()
    {
        // The plainest version of the attack: the file that was proven is overwritten with
        // something else before the session gets to it. Nothing about the path changes, so the
        // File.Exists check this replaced could not see it.
        var verifiedPath = Path.Combine(_tempDir, "swapped-companion.exe");
        var publisherHash = Sha256Hex([(byte)'M', (byte)'Z', 0x01]);
        WriteBytes(verifiedPath, "attacker-companion"u8.ToArray());

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.VerifiedPath, verifiedPath, publisherHash));

        Assert.Null(session.ElevationGateway);
    }

    [Fact]
    public async Task VerifiedPath_JunctionRepointedAfterVerification_StillLaunchesTheHashedFile()
    {
        // The attack that beat the first version of the payload-crossing fix. Creating a junction
        // needs no privilege, so an ordinary same-user process can rename the extraction directory,
        // drop a junction of the same name in its place, and repoint that junction later. Holding
        // an open handle on the companion does not stop that: the handle pins the file object, and
        // deleting a junction never touches the files under its target. So the defence cannot be
        // the handle alone. It has to be the path Windows reports for the handle, which has every
        // junction already followed.
        var realDir = Directory.CreateDirectory(Path.Combine(_tempDir, "companion-real")).FullName;
        var evilDir = Directory.CreateDirectory(Path.Combine(_tempDir, "companion-evil")).FullName;
        var link = Path.Combine(_tempDir, "companion");
        if (!TestJunction.TryCreate(link, realDir))
            Assert.Skip("Could not create an NTFS directory junction in this environment.");

        try
        {
            var publisherBytes = "publisher-companion"u8.ToArray();
            var attackerBytes = "attacker-companion"u8.ToArray();
            var (realCompanion, hash) = WriteCompanion(Path.Combine(realDir, CompanionFileName), publisherBytes);
            WriteBytes(Path.Combine(evilDir, CompanionFileName), attackerBytes);

            var junctionedPath = Path.Combine(link, CompanionFileName);
            var resolvedCompanion = ResolveFinalPath(realCompanion);

            await using (var session = EngineSession.BindToPipe(
                pipeName: null, WriteManifest(),
                Options(ElevationCompanionPolicy.VerifiedPath, junctionedPath, hash)))
            {
                Assert.NotNull(session.ElevationGateway);

                // The attacker repoints the junction after the session has verified and while it
                // still holds the handle. If this stopped working the test would prove nothing.
                TestJunction.Repoint(link, evilDir);
                Assert.Equal(attackerBytes, ReadBytes(junctionedPath));

                // Assert on the file that would actually be launched: read back the exact string
                // the gateway hands the process launcher, after the repoint.
                var launchPath = LaunchPathOf(session);
                Assert.Equal(resolvedCompanion, launchPath);
                Assert.NotEqual(junctionedPath, launchPath);
                Assert.Equal(publisherBytes, ReadBytes(launchPath));
            }
        }
        finally
        {
            // Remove the reparse point before teardown. A recursive delete of a directory that
            // still contains a junction fails with UnauthorizedAccessException, which would hide
            // the real assertion result behind a teardown crash.
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    [Fact]
    public async Task VerifiedPath_HoldsCompanionHandle_SoItCannotBeReplacedBeforeLaunch()
    {
        // The other half of the binding, and not the smaller half. The resolved path stops the
        // second open being redirected somewhere else, but on its own it stops nothing: delete the
        // file at the resolved path, drop a different file with the same name, and launching that
        // path runs the attacker's file. The held handle is what makes replacement, rename and
        // delete fail. Measured on this machine: a process starts normally while such a handle is
        // held, through both CreateProcessW and ShellExecuteEx, which is why the handle can be
        // kept rather than dropped before the launch.
        var (verifiedPath, hash) = WriteCompanion(
            Path.Combine(_tempDir, "held-companion.exe"), "publisher-companion"u8.ToArray());

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.VerifiedPath, verifiedPath, hash));

        Assert.NotNull(session.ElevationGateway);
        AssertCannotBeWrittenOrDeleted(verifiedPath);
    }

    [Theory]
    // The file is gone by the time the session looks (HashBoundFileStatus.FileNotFound).
    [InlineData(false, "3333333333333333333333333333333333333333333333333333333333333333")]
    // The file is there and its bytes are not the ones that were proven (HashMismatch).
    [InlineData(true, "3333333333333333333333333333333333333333333333333333333333333333")]
    // A path with no digest at all: nothing to prove the bytes against (MalformedExpectedHash).
    [InlineData(true, null)]
    public async Task CompanionThatDoesNotVerify_NeverFallsBackToTheAmbientProbe(
        bool createFile, string? suppliedHash)
    {
        // AmbientAllowed is the most permissive policy there is, and a companion is sitting beside
        // the engine ready to be picked up. A companion path that fails to verify must still leave
        // the session with no gateway: not the probe, and not the unverified path either. Falling
        // back to either would hand an unchecked binary a SYSTEM launch, which is the whole thing
        // the check exists to stop.
        Assert.True(File.Exists(_plantedCompanionPath), "test setup: planted companion must exist");

        var companionPath = Path.Combine(_tempDir, $"unverifiable_{Guid.NewGuid():N}.exe");
        if (createFile)
            WriteBytes(companionPath, "attacker-companion"u8.ToArray());

        await using var session = EngineSession.BindToPipe(
            pipeName: null, WriteManifest(),
            Options(ElevationCompanionPolicy.AmbientAllowed, companionPath, suppliedHash));

        Assert.Null(session.ElevationGateway);
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
