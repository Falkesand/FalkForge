namespace FalkForge.Ui.Abstractions.Tests;

using FalkForge.Engine.Protocol;
using Xunit;

public class IInstallerEngineTests
{
    [Fact]
    public async Task DetectAsync_CanBeInvoked()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        var result = await engine.DetectAsync();

        Assert.Equal(InstallState.NotInstalled, result.State);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Empty(result.Features);
    }

    [Fact]
    public async Task PlanAsync_CanBeInvoked()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        var result = await engine.PlanAsync(InstallAction.Install);

        Assert.Single(result.PackageActions);
        Assert.Equal("Install TestPackage", result.PackageActions[0]);
        Assert.Equal(1024L, result.TotalDiskSpaceRequired);
    }

    [Fact]
    public async Task ApplyAsync_CanBeInvoked()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        var result = await engine.ApplyAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Manifest_CanBeAccessed()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        var manifest = engine.Manifest;

        Assert.Equal("TestProduct", manifest.Name);
        Assert.Equal("TestCorp", manifest.Manufacturer);
    }

    [Fact]
    public void DetectedState_CanBeAccessed()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        Assert.Equal(InstallState.NotInstalled, engine.DetectedState);
    }

    [Fact]
    public void Features_CanBeAccessed()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        Assert.Empty(engine.Features);
    }

    [Fact]
    public void InstallDirectory_CanBeSetAndRetrieved()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        engine.InstallDirectory = @"D:\Custom";

        Assert.Equal(@"D:\Custom", engine.InstallDirectory);
    }

    [Fact]
    public void Cancel_CanBeInvoked()
    {
        var testEngine = new TestInstallerEngine();
        IInstallerEngine engine = testEngine;

        engine.Cancel();

        Assert.True(testEngine.CancelCalled);
    }

    [Fact]
    public async Task ShutdownAsync_CanBeInvoked()
    {
        IInstallerEngine engine = new TestInstallerEngine();

        var exitCode = await engine.ShutdownAsync();

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Phase_CanBeSubscribed()
    {
        IInstallerEngine engine = new TestInstallerEngine();
        var completed = false;

        engine.Phase.Subscribe(new TestObserver<EnginePhase>(onCompleted: () => completed = true));

        Assert.True(completed);
    }

    [Fact]
    public void Progress_CanBeSubscribed()
    {
        IInstallerEngine engine = new TestInstallerEngine();
        var completed = false;

        engine.Progress.Subscribe(new TestObserver<InstallProgress>(onCompleted: () => completed = true));

        Assert.True(completed);
    }

    [Fact]
    public void StatusMessage_CanBeSubscribed()
    {
        IInstallerEngine engine = new TestInstallerEngine();
        var completed = false;

        engine.StatusMessage.Subscribe(new TestObserver<string>(onCompleted: () => completed = true));

        Assert.True(completed);
    }

    private sealed class TestObserver<T> : IObserver<T>
    {
        private readonly Action? _onCompleted;

        public TestObserver(Action? onCompleted = null)
        {
            _onCompleted = onCompleted;
        }

        public void OnCompleted() => _onCompleted?.Invoke();
        public void OnError(Exception error) { }
        public void OnNext(T value) { }
    }
}
