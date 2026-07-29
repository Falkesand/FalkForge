namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Contract tests for the channel/store testing fakes:
/// <see cref="FakeUiChannel"/> and <see cref="InMemoryJournalStore"/>.
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
}
