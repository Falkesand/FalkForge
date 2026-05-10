namespace FalkForge.Ui.Tests.ViewModels;

using System;
using System.Threading.Tasks;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

public class EngineClientLogPathTests
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

    private static PipeConnectionOptions CreateOptions() => new()
    {
        PipeName = "test-pipe",
        SharedSecret = [1, 2, 3]
    };

    [Fact]
    public async Task LogPath_DefaultsToNull_WhenNotProvided()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        Assert.Null(client.LogPath);
    }

    [Fact]
    public async Task LogPath_ReturnsConstructorValue_WhenProvided()
    {
        const string expected = @"C:\Temp\engine.log";

        await using var client = new EngineClient(CreateOptions(), CreateManifest(), expected);

        Assert.Equal(expected, client.LogPath);
    }
}
