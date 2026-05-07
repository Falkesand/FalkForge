namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Fluent builder for <see cref="IInstallerPipeline"/>. Accepts port implementations
/// and, in future slices, phase-step registrations. Calling <see cref="Build"/>
/// validates required ports and returns the configured pipeline.
/// </summary>
public sealed class InstallerPipelineBuilder
{
    private ISystemClock? _clock;
    private IRandomSource? _random;
    private IRollbackJournalStore? _journalStore;
    private IPayloadCache? _payloadCache;
    private IPayloadSource? _payloadSource;
    private ILayoutStore? _layoutStore;
    private IUiChannel? _uiChannel;
    private IElevatedCommandGateway? _elevationGateway;

    /// <summary>
    /// Registers the <see cref="ISystemClock"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithClock(ISystemClock clock)
    {
        _clock = clock;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IRandomSource"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithRandom(IRandomSource random)
    {
        _random = random;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IRollbackJournalStore"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithJournalStore(IRollbackJournalStore store)
    {
        _journalStore = store;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IPayloadCache"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithPayloadCache(IPayloadCache cache)
    {
        _payloadCache = cache;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IPayloadSource"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithPayloadSource(IPayloadSource source)
    {
        _payloadSource = source;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="ILayoutStore"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithLayoutStore(ILayoutStore store)
    {
        _layoutStore = store;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IUiChannel"/> implementation.
    /// </summary>
    public InstallerPipelineBuilder WithUiChannel(IUiChannel channel)
    {
        _uiChannel = channel;
        return this;
    }

    /// <summary>
    /// Registers the <see cref="IElevatedCommandGateway"/> implementation.
    /// Optional — elevation is skipped when not provided.
    /// </summary>
    public InstallerPipelineBuilder WithElevationGateway(IElevatedCommandGateway gateway)
    {
        _elevationGateway = gateway;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured <see cref="IInstallerPipeline"/>.
    /// </summary>
    public IInstallerPipeline Build() => new InstallerPipeline(
        _clock,
        _random,
        _journalStore,
        _payloadCache,
        _payloadSource,
        _layoutStore,
        _uiChannel,
        _elevationGateway);
}
