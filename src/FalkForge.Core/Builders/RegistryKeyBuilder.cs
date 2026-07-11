using System.Linq;
using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class RegistryKeyBuilder
{
    private readonly List<RegistryEntryModel> _entries = [];
    private readonly string _key;
    private readonly RegistryRoot _root;

    internal RegistryKeyBuilder(RegistryRoot root, string key)
    {
        _root = root;
        _key = key;
    }

    public RegistryKeyBuilder Value(string name, string value, RegistryValueType type = RegistryValueType.String)
    {
        _entries.Add(new RegistryEntryModel
            { Root = _root, Key = _key, ValueName = name, Value = value, ValueType = type });
        return this;
    }

    public RegistryKeyBuilder Value(string name, MsiProperty property)
    {
        return Value(name, property.ToString());
    }

    public RegistryKeyBuilder DWord(string name, int value)
    {
        _entries.Add(new RegistryEntryModel
            { Root = _root, Key = _key, ValueName = name, Value = value, ValueType = RegistryValueType.DWord });
        return this;
    }

    public RegistryKeyBuilder DefaultValue(string value)
    {
        _entries.Add(new RegistryEntryModel
            { Root = _root, Key = _key, ValueName = null, Value = value, ValueType = RegistryValueType.String });
        return this;
    }

    public RegistryKeyBuilder Binary(string name, byte[] value)
    {
        _entries.Add(new RegistryEntryModel
            { Root = _root, Key = _key, ValueName = name, Value = value, ValueType = RegistryValueType.Binary });
        return this;
    }

    public RegistryKeyBuilder MultiString(string name, IEnumerable<string> values)
    {
        _entries.Add(new RegistryEntryModel
        {
            Root = _root,
            Key = _key,
            ValueName = name,
            Value = values.ToArray(),
            ValueType = RegistryValueType.MultiString,
        });
        return this;
    }

    public RegistryKeyBuilder ExpandString(string name, string value)
    {
        _entries.Add(new RegistryEntryModel
        {
            Root = _root, Key = _key, ValueName = name, Value = value, ValueType = RegistryValueType.ExpandString,
        });
        return this;
    }

    // Intentionally no QWord(...) helper: the MSI Registry table has no native REG_QWORD
    // encoding (only SZ, EXPAND_SZ, MULTI_SZ, DWORD, and BINARY are representable per the
    // Windows Installer "Registry Table" reference). Store 64-bit values via
    // Binary(name, BitConverter.GetBytes(value)) instead. RegistryValueType.QWord still
    // exists on the enum for completeness, but RegistryTableProducer fails the compile
    // loudly if an entry with that type reaches it, rather than truncating to 32 bits or
    // silently writing a wrong-typed value.

    internal IReadOnlyList<RegistryEntryModel> Build()
    {
        return _entries;
    }
}