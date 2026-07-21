namespace FalkForge.Engine.Tests;

using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// Bug #56, Stage 2 — the self-extract bootstrapper (<c>Program.cs</c>) unpacks the bundle's payloads
/// to a per-run <c>cacheDir</c> and must forward that root to the engine via
/// <see cref="EngineSessionOptions.PayloadRoot"/>. <c>Program.Main</c> runs only with a real bundle exe,
/// so what is reachable in a unit test is the wiring it depends on: that
/// <see cref="EngineSession.BindToPipe"/> forwards the option into the pipeline context. When no root is
/// supplied (the <c>--manifest</c> / <c>forge plan</c> / offline-layout path) the pipeline must stay on
/// the manifest's authoritative SourcePath.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EngineSessionPayloadRootTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionPayloadRootTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_PayloadRoot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string WriteManifest()
    {
        var manifest = new InstallerManifest
        {
            Name = "PayloadRoot",
            Manufacturer = "Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(), // fresh per manifest so the per-bundle instance lock never collides
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = []
        };

        var manifestPath = Path.Combine(_tempDir, $"manifest_{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest));
        return manifestPath;
    }

    [Fact]
    public async Task BindToPipe_WithPayloadRootOption_ForwardsExtractionRootIntoPipeline()
    {
        var cacheDir = Path.Combine(_tempDir, "cache", Guid.NewGuid().ToString("N"));

        await using var session = EngineSession.BindToPipe(
            pipeName: null,
            WriteManifest(),
            new EngineSessionOptions
            {
                PayloadRoot = cacheDir,
                LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
                WriteJournal = false
            });

        Assert.Equal(cacheDir, session.PayloadRoot);
    }

    [Fact]
    public async Task BindToPipe_WithoutPayloadRootOption_LeavesPipelineOnManifestSourcePath()
    {
        await using var session = EngineSession.BindToPipe(
            pipeName: null,
            WriteManifest(),
            new EngineSessionOptions
            {
                LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log"),
                WriteJournal = false
            });

        Assert.Null(session.PayloadRoot);
    }
}
