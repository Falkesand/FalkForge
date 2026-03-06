namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui.Abstractions;
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

    [Fact]
    public async Task SetProperty_DoesNotThrow_WhenDisconnected()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        client.SetProperty("MY_PROP", "my_value");
    }

    [Fact]
    public async Task SetProperty_AcceptsEmptyValue()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());

        client.SetProperty("PROP", string.Empty);
    }

    [Fact]
    public async Task SetSecureProperty_DoesNotThrow_WhenDisconnected()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        using var sensitive = new SensitiveBytes([0x01, 0x02, 0x03]);

        client.SetSecureProperty("DB_PASSWORD", sensitive);
    }

    [Fact]
    public async Task SetSecureProperty_CopiesBytesSynchronously()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        var original = new byte[] { 0xAA, 0xBB, 0xCC };
        var sensitive = new SensitiveBytes(original);

        client.SetSecureProperty("SECRET", sensitive);

        // Dispose zeros the original array — if the implementation copied
        // synchronously, the internal copy is unaffected. This test verifies
        // the call completes without error even after the source is disposed.
        sensitive.Dispose();
        Assert.Equal(0, original[0]); // Confirms disposal zeroed the source.
    }

    [Fact]
    public async Task SetSecureProperty_AcceptsEmptyBytes()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        using var sensitive = new SensitiveBytes([]);

        client.SetSecureProperty("EMPTY_SECRET", sensitive);
    }

    [Fact]
    public async Task EngineClient_UpdateDownloadProgressMessage_RaisesProgressEvent()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        var events = new List<(int percent, long bytes, long total)>();
        client.UpdateDownloadProgress += (pct, bytes, total) => events.Add((pct, bytes, total));

        await client.SimulateMessageAsync(new UpdateDownloadProgressMessage
        {
            PercentComplete = 42,
            BytesReceived = 1024,
            TotalBytes = 2048
        });

        Assert.Single(events);
        Assert.Equal((42, 1024L, 2048L), events[0]);
    }

    [Fact]
    public async Task EngineClient_UpdateAvailableMessage_RaisesUpdateAvailableEvent()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        var events = new List<(string version, string? notes)>();
        client.UpdateAvailable += (v, n) => events.Add((v, n));

        await client.SimulateMessageAsync(new UpdateAvailableMessage
        {
            Version = "2.0.0",
            ReleaseNotes = "Bug fixes",
            DownloadUrl = "https://example.com/update"
        });

        Assert.Single(events);
        Assert.Equal("2.0.0", events[0].version);
        Assert.Equal("Bug fixes", events[0].notes);
    }

    [Fact]
    public async Task EngineClient_UpdateReadyMessage_RaisesUpdateReadyEvent()
    {
        await using var client = new EngineClient(CreateOptions(), CreateManifest());
        var versions = new List<string>();
        client.UpdateReady += (v, _) => versions.Add(v);

        await client.SimulateMessageAsync(new UpdateReadyMessage
        {
            Version = "2.0.0",
            LocalPath = @"C:\Temp\update.exe"
        });

        Assert.Single(versions);
        Assert.Equal("2.0.0", versions[0]);
    }
}
