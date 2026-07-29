namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Platform;
using Xunit;

/// <summary>
/// Compile-time contract tests for the RFC Cycle 1 port interfaces.
/// Each "fake" class proves the interface is implementable and its members exist.
/// No runtime logic — RED when interfaces are absent, GREEN once defined.
/// </summary>
public sealed class PipelinePortsContractTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // ISystemClock
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeClock : ISystemClock
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public DateTimeOffset UtcNow => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    [Fact]
    public void SystemClock_Interface_Is_Implementable()
    {
        ISystemClock clock = new FakeClock();
        Assert.True(clock.UtcNow <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IRollbackJournalStore
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeJournalStore : IRollbackJournalStore
    {
        private readonly List<JournalEntry> _entries = [];

        public Result<Unit> Append(JournalEntry entry)
        {
            _entries.Add(entry);
            return Unit.Value;
        }

        public Result<IReadOnlyList<JournalEntry>> LoadAll()
            => Result<IReadOnlyList<JournalEntry>>.Success(_entries.AsReadOnly());

        public Result<Unit> Clear() { _entries.Clear(); return Unit.Value; }
        public void Dispose() { }
    }

    [Fact]
    public void JournalStore_Interface_Is_Implementable()
    {
        IRollbackJournalStore store = new FakeJournalStore();
        var entry = new JournalEntry
        {
            EntryType = JournalEntryType.PackageInstalled,
            Description = "test"
        };
        var appendResult = store.Append(entry);
        Assert.True(appendResult.IsSuccess);

        var loadResult = store.LoadAll();
        Assert.True(loadResult.IsSuccess);
        Assert.Single(loadResult.Value);

        var clearResult = store.Clear();
        Assert.True(clearResult.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IUiChannel
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeUiChannel : IUiChannel
    {
        public List<PipelineEvent> SentEvents { get; } = [];

        public void SetSessionCorrelationId(Guid id) { /* no-op in contract test */ }

        public Task SendAsync(PipelineEvent evt, CancellationToken ct)
        {
            SentEvents.Add(evt);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<UiRequest> ReadRequestsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync() => default;
    }

    [Fact]
    public async Task UiChannel_Interface_Is_Implementable()
    {
        IUiChannel channel = new FakeUiChannel();
        var evt = new PipelineEvent.PhaseChanged(EnginePhase.Detecting);
        await channel.SendAsync(evt, CancellationToken.None);
        Assert.Single(((FakeUiChannel)channel).SentEvents);
        await channel.DisposeAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IElevatedCommandGateway
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeElevationGateway : IElevatedCommandGateway
    {
        public Task<Result<Unit>> StartAsync(CancellationToken ct) => Task.FromResult(Result<Unit>.Success(Unit.Value));

        public void SetCorrelationId(Guid id) { /* no-op for contract test double */ }

        public Task<Result<byte[]>> SendCommandAsync(
            string commandName,
            byte[] payload,
            IProgress<int>? progress,
            CancellationToken ct)
            => Task.FromResult(Result<byte[]>.Success(Array.Empty<byte>()));

        public ValueTask DisposeAsync() => default;
    }

    [Fact]
    public async Task ElevationGateway_Interface_Is_Implementable()
    {
        IElevatedCommandGateway gw = new FakeElevationGateway();
        var startResult = await gw.StartAsync(CancellationToken.None);
        Assert.True(startResult.IsSuccess);

        var cmdResult = await gw.SendCommandAsync("TestCmd", [], progress: null, CancellationToken.None);
        Assert.True(cmdResult.IsSuccess);
        await gw.DisposeAsync();
    }
}
