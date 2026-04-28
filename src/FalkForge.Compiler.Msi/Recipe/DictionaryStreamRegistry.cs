using System.Collections.Frozen;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Default <see cref="IStreamRegistry"/> backed by an ordinal dictionary.
/// Stream names are case-sensitive (MSI streams are ordinal). Duplicate
/// registrations throw <see cref="InvalidOperationException"/> rather than
/// silently overwriting — silent overwrite would corrupt the produced MSI.
/// </summary>
internal sealed class DictionaryStreamRegistry : IStreamRegistry
{
    private readonly Dictionary<string, StreamSource> _streams = new(StringComparer.Ordinal);

    public void Register(string streamName, StreamSource source)
    {
        ArgumentNullException.ThrowIfNull(streamName);
        ArgumentNullException.ThrowIfNull(source);

        if (!_streams.TryAdd(streamName, source))
        {
            throw new InvalidOperationException(
                $"Stream '{streamName}' has already been registered.");
        }
    }

    public bool TryGet(string streamName, out StreamSource source)
    {
        ArgumentNullException.ThrowIfNull(streamName);
        return _streams.TryGetValue(streamName, out source!);
    }

    public IReadOnlyDictionary<string, StreamSource> Snapshot()
    {
        // FrozenDictionary gives an immutable, lookup-optimized snapshot.
        return _streams.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
