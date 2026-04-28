namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Append-only registry of MSI stream payloads keyed by stream name. Producers
/// register streams during recipe build; <see cref="Snapshot"/> exposes a
/// read-only view consumed by <see cref="MsiDatabaseRecipe.Streams"/>.
/// Implementations must reject duplicate stream names — a duplicate indicates
/// a producer bug.
/// </summary>
internal interface IStreamRegistry
{
    /// <summary>Register a stream payload under the given name. Throws if the name was already registered.</summary>
    void Register(string streamName, StreamSource source);

    /// <summary>Attempt to fetch a previously registered stream by name.</summary>
    bool TryGet(string streamName, out StreamSource source);

    /// <summary>Frozen read-only snapshot of all registered streams.</summary>
    IReadOnlyDictionary<string, StreamSource> Snapshot();
}
