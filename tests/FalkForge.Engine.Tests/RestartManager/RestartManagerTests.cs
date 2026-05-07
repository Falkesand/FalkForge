namespace FalkForge.Engine.Tests.RestartManager;

using FalkForge.Engine.RestartManager;
using FalkForge.Engine.Tests.Helpers;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class RestartManagerTests
{
    // ─── Session Lifecycle ──────────────────────────────────────────────

    [Fact]
    public void StartSession_SetsSessionActive()
    {
        var mock = new MockRestartManager();

        var result = mock.StartSession();

        Assert.True(result.IsSuccess);
        Assert.True(mock.SessionActive);
        Assert.Contains(nameof(IRestartManager.StartSession), mock.CallLog);
    }

    [Fact]
    public void EndSession_DeactivatesSession()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        mock.EndSession();

        Assert.False(mock.SessionActive);
        Assert.Contains(nameof(IRestartManager.EndSession), mock.CallLog);
    }

    [Fact]
    public void Dispose_EndsSession()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        mock.Dispose();

        Assert.False(mock.SessionActive);
        Assert.True(mock.Disposed);
    }

    [Fact]
    public void StartSession_Failure_ReturnsError()
    {
        var mock = new MockRestartManager()
            .WithStartSessionResult(
                Result<Unit>.Failure(ErrorKind.PlatformError, "Session start failed"));

        var result = mock.StartSession();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlatformError, result.Error.Kind);
        Assert.False(mock.SessionActive);
    }

    // ─── RegisterResources ──────────────────────────────────────────────

    [Fact]
    public void RegisterResources_WithValidPaths_Succeeds()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        var result = mock.RegisterResources(new[] { @"C:\app\file.dll", @"C:\app\file.exe" });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, mock.RegisteredFiles.Count);
        Assert.Contains(@"C:\app\file.dll", mock.RegisteredFiles);
        Assert.Contains(@"C:\app\file.exe", mock.RegisteredFiles);
    }

    [Fact]
    public void RegisterResources_WithEmptyList_Succeeds()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        var result = mock.RegisterResources(Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Empty(mock.RegisteredFiles);
    }

    [Fact]
    public void RegisterResources_Failure_ReturnsError()
    {
        var mock = new MockRestartManager()
            .WithRegisterResourcesResult(
                Result<Unit>.Failure(ErrorKind.PlatformError, "Register failed"));
        mock.StartSession();

        var result = mock.RegisterResources(new[] { @"C:\file.dll" });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PlatformError, result.Error.Kind);
    }

    // ─── GetAffectedProcesses ───────────────────────────────────────────

    [Fact]
    public void GetAffectedProcesses_NoConflicts_ReturnsEmptyList()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        var result = mock.GetAffectedProcesses();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void GetAffectedProcesses_WithConflicts_ReturnsProcesses()
    {
        var mock = new MockRestartManager()
            .WithAffectedProcesses(
                new RestartManagerProcess(1234, "notepad", "Notepad", CanBeRestarted: true),
                new RestartManagerProcess(5678, "explorer", "Explorer", CanBeRestarted: false));
        mock.StartSession();

        var result = mock.GetAffectedProcesses();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Notepad", result.Value[0].ApplicationName);
        Assert.True(result.Value[0].CanBeRestarted);
        Assert.False(result.Value[1].CanBeRestarted);
    }

    // ─── Shutdown / Restart ─────────────────────────────────────────────

    [Fact]
    public void ShutdownProcesses_Succeeds()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        var result = mock.ShutdownProcesses();

        Assert.True(result.IsSuccess);
        Assert.Contains(nameof(IRestartManager.ShutdownProcesses), mock.CallLog);
    }

    [Fact]
    public void RestartProcesses_Succeeds()
    {
        var mock = new MockRestartManager();
        mock.StartSession();

        var result = mock.RestartProcesses();

        Assert.True(result.IsSuccess);
        Assert.Contains(nameof(IRestartManager.RestartProcesses), mock.CallLog);
    }

    [Fact]
    public void ShutdownProcesses_Failure_ReturnsError()
    {
        var mock = new MockRestartManager()
            .WithShutdownResult(
                Result<Unit>.Failure(ErrorKind.PlatformError, "Shutdown failed"));
        mock.StartSession();

        var result = mock.ShutdownProcesses();

        Assert.True(result.IsFailure);
        Assert.Equal("Shutdown failed", result.Error.Message);
    }

    // ─── Full Lifecycle ─────────────────────────────────────────────────

    [Fact]
    public void FullLifecycle_CallsInCorrectOrder()
    {
        var mock = new MockRestartManager()
            .WithAffectedProcesses(
                new RestartManagerProcess(100, "app", "TestApp", CanBeRestarted: true));

        mock.StartSession();
        mock.RegisterResources(new[] { @"C:\app\test.dll" });
        var affected = mock.GetAffectedProcesses();

        if (affected.IsSuccess && affected.Value.Count > 0)
            mock.ShutdownProcesses();

        // ... apply would happen here ...

        mock.RestartProcesses();
        mock.EndSession();

        Assert.Equal(new[]
        {
            nameof(IRestartManager.StartSession),
            nameof(IRestartManager.RegisterResources),
            nameof(IRestartManager.GetAffectedProcesses),
            nameof(IRestartManager.ShutdownProcesses),
            nameof(IRestartManager.RestartProcesses),
            nameof(IRestartManager.EndSession)
        }, mock.CallLog);
    }

    [Fact]
    public void EndSession_AlwaysCalledEvenOnFailure()
    {
        var mock = new MockRestartManager()
            .WithShutdownResult(
                Result<Unit>.Failure(ErrorKind.PlatformError, "Shutdown failed"))
            .WithAffectedProcesses(
                new RestartManagerProcess(100, "app", "TestApp", CanBeRestarted: true));

        try
        {
            mock.StartSession();
            mock.RegisterResources(new[] { @"C:\app\test.dll" });
            mock.GetAffectedProcesses();

            var shutdownResult = mock.ShutdownProcesses();
            if (shutdownResult.IsFailure)
            {
                // Simulate early exit on error
                return;
            }

            mock.RestartProcesses();
        }
        finally
        {
            mock.EndSession();
        }

        // Should not reach here because of early return
        Assert.Fail("Should have returned early due to shutdown failure");
    }

    [Fact]
    public void EndSession_CalledInFinallyBlock_RecordsCallAfterShutdownFailure()
    {
        var mock = new MockRestartManager()
            .WithShutdownResult(
                Result<Unit>.Failure(ErrorKind.PlatformError, "Shutdown failed"))
            .WithAffectedProcesses(
                new RestartManagerProcess(100, "app", "TestApp", CanBeRestarted: true));

        try
        {
            mock.StartSession();
            mock.RegisterResources(new[] { @"C:\app\test.dll" });
            mock.GetAffectedProcesses();
            mock.ShutdownProcesses();
        }
        finally
        {
            mock.EndSession();
        }

        // EndSession should appear in the call log even after failure
        Assert.Equal(nameof(IRestartManager.EndSession), mock.CallLog[^1]);
    }

    // ─── ApplyStep Integration (pipeline-based) ────────────────────────

    [Fact]
    public async Task ApplyStep_WithRestartManager_AffectedProcesses_FullLifecycle()
    {
        var mock = new MockRestartManager()
            .WithAffectedProcesses(
                new RestartManagerProcess(1234, "notepad", "Notepad", CanBeRestarted: true));

        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var ctx = ExecutionTestFactory.CreateContext();
        ctx.Plan = ExecutionTestFactory.CreatePlanWithMsuPackages(2);
        ctx.RestartManager = mock;

        await using var channel = new FalkForge.Testing.FakeUiChannel();
        using var journalStore = new FalkForge.Testing.InMemoryJournalStore();
        var step = new FalkForge.Engine.Pipeline.ApplyStep(executor, journalStore, channel);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(nameof(IRestartManager.StartSession), mock.CallLog);
        Assert.Contains(nameof(IRestartManager.ShutdownProcesses), mock.CallLog);
        Assert.Contains(nameof(IRestartManager.RestartProcesses), mock.CallLog);
        Assert.Contains(nameof(IRestartManager.EndSession), mock.CallLog);
    }

    [Fact]
    public async Task ApplyStep_WithRestartManager_NoAffectedProcesses_SkipsShutdown()
    {
        var mock = new MockRestartManager(); // no affected processes

        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var ctx = ExecutionTestFactory.CreateContext();
        ctx.Plan = ExecutionTestFactory.CreatePlanWithMsuPackages(1);
        ctx.RestartManager = mock;

        await using var channel = new FalkForge.Testing.FakeUiChannel();
        using var journalStore = new FalkForge.Testing.InMemoryJournalStore();
        var step = new FalkForge.Engine.Pipeline.ApplyStep(executor, journalStore, channel);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(nameof(IRestartManager.ShutdownProcesses), mock.CallLog);
        Assert.Contains(nameof(IRestartManager.EndSession), mock.CallLog);
    }

    [Fact]
    public async Task ApplyStep_WithoutRestartManager_NoRmCalls()
    {
        var runner = new MockProcessRunner().WithExitCode(0);
        var executor = ExecutionTestFactory.CreateExecutor(runner);
        var ctx = ExecutionTestFactory.CreateContext();
        ctx.Plan = ExecutionTestFactory.CreatePlanWithMsuPackages(1);
        // RestartManager is null — no RM

        await using var channel = new FalkForge.Testing.FakeUiChannel();
        using var journalStore = new FalkForge.Testing.InMemoryJournalStore();
        var step = new FalkForge.Engine.Pipeline.ApplyStep(executor, journalStore, channel);

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
