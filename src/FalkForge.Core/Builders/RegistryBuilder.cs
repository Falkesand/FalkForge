using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class RegistryBuilder
{
    private readonly List<RegistryEntryModel> _entries = [];

    public RegistryBuilder Key(RegistryRoot root, string key, Action<RegistryKeyBuilder> configure)
    {
        var builder = new RegistryKeyBuilder(root, key);
        configure(builder);
        _entries.AddRange(builder.Build());
        return this;
    }

    internal IReadOnlyList<RegistryEntryModel> Build()
    {
        return _entries;
    }
}