namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Contract tests for the channel/store/source testing fakes:
/// <see cref="FakeUiChannel"/>, <see cref="InMemoryJournalStore"/>,
/// <see cref="InMemoryPayloadSource"/>, <see cref="DictPayloadCache"/>,
/// and <see cref="InMemoryLayoutStore"/>.
/// </summary>
public sealed class TestingFakesChannelStoreTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // FakeUiChannel
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FakeUiChannel_SendAsync_AccumulatesEvents()
    {
        await using var ch = new FakeUiChannel();
        await ch.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Detecting), CancellationToken.None);
        await ch.SendAsync(new PipelineEvent.Progress(50, "halfway"), CancellationToken.None);
        Assert.Equal(2, ch.SentEvents.Count);
        Assert.IsType<PipelineEvent.PhaseChanged>(ch.SentEvents[0]);
    }

    [Fact]
    public async Task FakeUiChannel_ReadRequestsAsync_YieldsEnqueuedRequests()
    {
        await using var ch = new FakeUiChannel();
        ch.EnqueueRequest(new UiRequest.Detect());
        ch.EnqueueRequest(new UiRequest.Apply());
        ch.Complete();

        var requests = new List<UiRequest>();
        await foreach (var req in ch.ReadRequestsAsync(CancellationToken.None))
            requests.Add(req);

        Assert.Equal(2, requests.Count);
        Assert.IsType<UiRequest.Detect>(requests[0]);
        Assert.IsType<UiRequest.Apply>(requests[1]);
    }

    [Fact]
    public async Task FakeUiChannel_ReadRequestsAsync_StopsAtShutdown()
    {
        await using var ch = new FakeUiChannel();
        ch.EnqueueRequest(new UiRequest.Detect());
        ch.EnqueueRequest(new UiRequest.Shutdown());
        ch.EnqueueRequest(new UiRequest.Apply()); // never yielded

        var requests = new List<UiRequest>();
        await foreach (var req in ch.ReadRequestsAsync(CancellationToken.None))
            requests.Add(req);

        // Detect + Shutdown but not Apply
        Assert.Equal(2, requests.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // InMemoryJournalStore
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InMemoryJournalStore_AppendAndLoadAll_RoundTrips()
    {
        using var store = new InMemoryJournalStore();
        var entry = new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "pkg"
        };
        var appendResult = store.Append(entry);
        Assert.True(appendResult.IsSuccess);

        var loadResult = store.LoadAll();
        Assert.True(loadResult.IsSuccess);
        Assert.Single(loadResult.Value);
        Assert.Equal("pkg", loadResult.Value[0].Description);
    }

    [Fact]
    public void InMemoryJournalStore_Clear_RemovesAll()
    {
        using var store = new InMemoryJournalStore();
        store.Append(new JournalEntry { EntryType = JournalEntryType.PackageInstalled, Description = "x" });
        store.Clear();
        var result = store.LoadAll();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // InMemoryPayloadSource
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryPayloadSource_DownloadAsync_WritesRegisteredContent()
    {
        var content = "hello-payload"u8.ToArray();
        var source = new InMemoryPayloadSource()
            .Register("https://example.com/pkg.msi", content, "abc");

        var dest = Path.GetTempFileName();
        try
        {
            var result = await source.DownloadAsync(
                "https://example.com/pkg.msi", "abc", dest, null, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(dest, result.Value);
            Assert.Equal(content, await File.ReadAllBytesAsync(dest));
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task InMemoryPayloadSource_DownloadAsync_ReturnsFailureForUnknownUrl()
    {
        var source = new InMemoryPayloadSource();
        var result = await source.DownloadAsync(
            "https://example.com/missing.msi", "", Path.GetTempFileName(),
            null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DictPayloadCache
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DictPayloadCache_StoreAndResolve_RoundTrips()
    {
        var cache = new DictPayloadCache();
        var id = Guid.NewGuid();
        cache.Store(id, "pkg", "sha256abc", @"C:\cache\pkg.msi");
        var result = cache.Resolve(id, "pkg", "sha256abc");
        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\cache\pkg.msi", result.Value);
    }

    [Fact]
    public void DictPayloadCache_Resolve_ReturnsFileNotFoundForMiss()
    {
        var cache = new DictPayloadCache();
        var result = cache.Resolve(Guid.NewGuid(), "pkg", "sha256");
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void DictPayloadCache_Remove_DeletesEntry()
    {
        var cache = new DictPayloadCache();
        var id = Guid.NewGuid();
        cache.Store(id, "p", "s", "path");
        cache.Remove(id, "p", "s");
        Assert.True(cache.Resolve(id, "p", "s").IsFailure);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // InMemoryLayoutStore
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryLayoutStore_WriteAndRead_RoundTrips()
    {
        var store = new InMemoryLayoutStore();
        var manifest = new InstallerManifest
        {
            Name = "MyApp",
            Manufacturer = "Acme",
            Version = "2.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = []
        };

        var writeResult = await store.WriteAsync(manifest, @"C:\layout", CancellationToken.None);
        Assert.True(writeResult.IsSuccess);

        var readResult = await store.ReadAsync(@"C:\layout", CancellationToken.None);
        Assert.True(readResult.IsSuccess);
        Assert.Equal("MyApp", readResult.Value.Name);
    }

    [Fact]
    public async Task InMemoryLayoutStore_Read_ReturnsFileNotFoundForMissingPath()
    {
        var store = new InMemoryLayoutStore();
        var result = await store.ReadAsync(@"C:\no-such-layout", CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }
}
