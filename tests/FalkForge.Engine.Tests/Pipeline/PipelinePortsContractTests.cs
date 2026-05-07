namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
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
    // IRandomSource
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeRandom : IRandomSource
    {
        public Guid NewGuid() => Guid.Empty;
        public void Fill(Span<byte> buffer) => buffer.Clear();
    }

    [Fact]
    public void RandomSource_Interface_Is_Implementable()
    {
        IRandomSource rng = new FakeRandom();
        Assert.Equal(Guid.Empty, rng.NewGuid());
        Span<byte> buf = stackalloc byte[16];
        rng.Fill(buf);
        Assert.True(buf.SequenceEqual(new byte[16]));
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
    // IPayloadCache
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakePayloadCache : IPayloadCache
    {
        private readonly Dictionary<string, string> _paths = [];

        public Result<string> Store(Guid bundleId, string packageId, string sha256, string sourceFilePath)
        {
            var key = $"{bundleId}:{packageId}:{sha256}";
            _paths[key] = sourceFilePath;
            return sourceFilePath;
        }

        public Result<string> Resolve(Guid bundleId, string packageId, string sha256)
        {
            var key = $"{bundleId}:{packageId}:{sha256}";
            return _paths.TryGetValue(key, out var path)
                ? Result<string>.Success(path)
                : Result<string>.Failure(ErrorKind.FileNotFound, "Not cached");
        }

        public Result<Unit> Remove(Guid bundleId, string packageId, string sha256)
        {
            var key = $"{bundleId}:{packageId}:{sha256}";
            _paths.Remove(key);
            return Unit.Value;
        }
    }

    [Fact]
    public void PayloadCache_Interface_Is_Implementable()
    {
        IPayloadCache cache = new FakePayloadCache();
        var bundleId = Guid.NewGuid();
        var storeResult = cache.Store(bundleId, "pkg1", "abc123", @"C:\pkg.msi");
        Assert.True(storeResult.IsSuccess);

        var resolveResult = cache.Resolve(bundleId, "pkg1", "abc123");
        Assert.True(resolveResult.IsSuccess);

        var missResult = cache.Resolve(bundleId, "pkg1", "deadbeef");
        Assert.True(missResult.IsFailure);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IPayloadSource
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakePayloadSource : IPayloadSource
    {
        public Task<Result<string>> DownloadAsync(
            string url,
            string expectedSha256,
            string destinationPath,
            IProgress<(long BytesReceived, long TotalBytes)>? progress,
            CancellationToken ct)
        {
            return Task.FromResult(Result<string>.Success(destinationPath));
        }
    }

    [Fact]
    public async Task PayloadSource_Interface_Is_Implementable()
    {
        IPayloadSource source = new FakePayloadSource();
        var result = await source.DownloadAsync(
            "https://example.com/pkg.msi",
            "abc123",
            @"C:\dest\pkg.msi",
            progress: null,
            CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ILayoutStore
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeLayoutStore : ILayoutStore
    {
        private InstallerManifest? _manifest;

        public Task<Result<Unit>> WriteAsync(InstallerManifest manifest, string layoutPath, CancellationToken ct)
        {
            _manifest = manifest;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public Task<Result<InstallerManifest>> ReadAsync(string layoutPath, CancellationToken ct)
            => Task.FromResult(_manifest is not null
                ? Result<InstallerManifest>.Success(_manifest)
                : Result<InstallerManifest>.Failure(ErrorKind.FileNotFound, "No manifest"));
    }

    [Fact]
    public async Task LayoutStore_Interface_Is_Implementable()
    {
        ILayoutStore store = new FakeLayoutStore();
        var manifest = new InstallerManifest
        {
            Name = "Test", Manufacturer = "M", Version = "1.0",
            BundleId = Guid.NewGuid(), UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine, Packages = []
        };
        var writeResult = await store.WriteAsync(manifest, @"C:\layout", CancellationToken.None);
        Assert.True(writeResult.IsSuccess);

        var readResult = await store.ReadAsync(@"C:\layout", CancellationToken.None);
        Assert.True(readResult.IsSuccess);
        Assert.Equal("Test", readResult.Value.Name);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IUiChannel
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeUiChannel : IUiChannel
    {
        public List<PipelineEvent> SentEvents { get; } = [];

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
