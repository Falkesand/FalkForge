namespace FalkForge.Engine.Tests;

using System.Reflection;
using System.Text.Json;
using FalkForge.Engine;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using Xunit;

/// <summary>
/// Proves that <see cref="EngineSession.BindToPipe"/> — the actual production entry point used by
/// <c>Program.cs</c>, not a hand-built copy of its wiring — constructs <c>MsiExecutor</c> and
/// <c>ExeExecutor</c> with a live accessor into the SAME <see cref="VariableStore"/> instance the
/// pipeline seeds, and wires <c>IPlatformServices</c>/<c>ISystemClock</c> into <c>DetectStep</c>.
/// <para>
/// <see cref="EngineSession.BindToPipe"/> cannot be driven end-to-end without a real named pipe
/// and a real EXE/MSI on disk, so these tests inspect the private state it actually constructs via
/// reflection rather than re-implementing the wiring by hand (which is exactly what let the
/// original defect — <c>MsiExecutor(static () =&gt; null, ...)</c> and
/// <c>new ExeExecutor(processRunner)</c> — ship silently: <c>ExeExecutorVariableWiringTests</c>
/// built its own executors and never touched <c>BindToPipe</c> at all).
/// </para>
/// <para>
/// Verified by mutation: reverting <c>EngineSession.BindToPipe.cs:152</c> to
/// <c>static () =&gt; null</c> fails <see cref="BindToPipe_MsiExecutor_ResolvesSameLiveVariableStoreAsPipeline"/>;
/// reverting <c>:164</c> to <c>new ExeExecutor(processRunner)</c> fails
/// <see cref="BindToPipe_ExeExecutor_ResolvesSameLiveVariableStoreAsPipeline"/>; deleting the
/// <c>.WithPlatformServices(platform)</c>/<c>.WithClock(...)</c> calls at <c>:292-293</c> fails
/// <see cref="BindToPipe_DetectStep_WiresPlatformServicesAndClock"/>.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EngineSessionExecutorWiringTests : IDisposable
{
    private readonly string _tempDir;

    public EngineSessionExecutorWiringTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge_Tests_ExecutorWiring", Guid.NewGuid().ToString("N"));
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
            Name = "ExecutorWiring",
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

    private EngineSessionOptions Options() => new()
    {
        LogPath = Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.log")
        // WriteJournal left at its default (true): ApplyStep/PackageExecutor is only constructed
        // by InstallerPipelineBuilder.Build() when a journal store is present, and the MsiExecutor/
        // ExeExecutor wiring under test lives inside that PackageExecutor.
    };

    private static object GetField(object target, string fieldName)
    {
        var type = target.GetType();
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        var value = field?.GetValue(target);
        return value ?? throw new InvalidOperationException(
            $"Field '{fieldName}' on '{type.FullName}' was null or missing — production wiring changed shape.");
    }

    private static object? GetFieldOrNull(object target, string fieldName) =>
        target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

    [Fact]
    public async Task BindToPipe_MsiExecutor_ResolvesSameLiveVariableStoreAsPipeline()
    {
        await using var session = EngineSession.BindToPipe(pipeName: null, WriteManifest(), Options());

        var pipeline = GetField(session, "_pipeline");
        var liveStore = (VariableStore)GetField(pipeline, "_variableStore");

        var applyStep = GetField(pipeline, "_applyStep");
        var packageExecutor = GetField(applyStep, "_executor");
        var msiExecutor = GetField(packageExecutor, "_msiExecutor");
        var msiAccessor = (Func<VariableStore?>)GetField(msiExecutor, "_variableStoreAccessor");

        Assert.Same(liveStore, msiAccessor());
    }

    [Fact]
    public async Task BindToPipe_ExeExecutor_ResolvesSameLiveVariableStoreAsPipeline()
    {
        await using var session = EngineSession.BindToPipe(pipeName: null, WriteManifest(), Options());

        var pipeline = GetField(session, "_pipeline");
        var liveStore = (VariableStore)GetField(pipeline, "_variableStore");

        var applyStep = GetField(pipeline, "_applyStep");
        var packageExecutor = GetField(applyStep, "_executor");
        var exeExecutor = GetField(packageExecutor, "_exeExecutor");
        var exeAccessor = (Func<VariableStore?>)GetField(exeExecutor, "_variableStoreAccessor");

        Assert.Same(liveStore, exeAccessor());
    }

    [Fact]
    public async Task BindToPipe_DetectStep_WiresPlatformServicesAndClock()
    {
        await using var session = EngineSession.BindToPipe(pipeName: null, WriteManifest(), Options());

        var pipeline = GetField(session, "_pipeline");
        var detectStep = GetField(pipeline, "_detectStep");

        Assert.NotNull(GetFieldOrNull(detectStep, "_platform"));
        Assert.NotNull(GetFieldOrNull(detectStep, "_clock"));
    }
}
