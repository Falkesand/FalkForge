namespace FalkInstaller.Ui.Tests.ViewModels;

using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Protocol.Transport;
using Xunit;

public class EngineClientTests
{
    private static InstallerManifest CreateManifest() => new()
    {
        Name = "TestProduct",
        Manufacturer = "TestCorp",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser
    };

    private static PipeConnectionOptions CreateOptions(string pipeName = "test-pipe") => new()
    {
        PipeName = pipeName,
        SharedSecret = [1, 2, 3]
    };

    [Fact]
    public async Task Manifest_ReturnsProvidedManifest()
    {
        var manifest = CreateManifest();
        await using var client = new EngineClient(CreateOptions(), manifest);

        Assert.Same(manifest, client.Manifest);
    }

    [Fact]
    public async Task DetectedState_DefaultsToNotInstalled()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.Equal(InstallState.NotInstalled, client.DetectedState);
    }

    [Fact]
    public async Task Features_DefaultsToEmpty()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.Empty(client.Features);
    }

    [Fact]
    public async Task InstallDirectory_DefaultsToEmpty()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.Equal(string.Empty, client.InstallDirectory);
    }

    [Fact]
    public async Task InstallDirectory_CanBeSet()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        client.InstallDirectory = @"C:\TestDir";

        Assert.Equal(@"C:\TestDir", client.InstallDirectory);
    }

    [Fact]
    public async Task Phase_IsObservable()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.NotNull(client.Phase);
    }

    [Fact]
    public async Task Progress_IsObservable()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.NotNull(client.Progress);
    }

    [Fact]
    public async Task StatusMessage_IsObservable()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.NotNull(client.StatusMessage);
    }
}
