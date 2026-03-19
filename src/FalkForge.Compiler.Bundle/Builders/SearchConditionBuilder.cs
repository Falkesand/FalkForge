namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

public sealed class SearchConditionBuilder
{
    private SearchConditionType _type;
    private string _path = "";
    private string? _value;
    private string? _comparison;

    public SearchConditionBuilder FileExists(string path)
    {
        _type = SearchConditionType.FileExists;
        _path = path;
        return this;
    }

    public SearchConditionBuilder FileVersion(string path, string comparison, string version)
    {
        _type = SearchConditionType.FileVersion;
        _path = path;
        _comparison = comparison;
        _value = version;
        return this;
    }

    public SearchConditionBuilder DirectoryExists(string path)
    {
        _type = SearchConditionType.DirectoryExists;
        _path = path;
        return this;
    }

    /// <summary>
    /// Checks whether a registry key or value exists.
    /// </summary>
    public SearchConditionBuilder RegistryExists(RegistryRoot root, string key, string? valueName = null)
    {
        _type = SearchConditionType.RegistryValue;
        _path = $@"{FormatRoot(root)}\{key}";
        _value = valueName;
        _comparison = "exists";
        return this;
    }

    /// <summary>
    /// Compares a registry value against an expected value using the specified comparison operator.
    /// The comparison is stored as "operator:expectedValue" (e.g., "&gt;=:2.0.0" or "=:Enterprise").
    /// </summary>
    public SearchConditionBuilder RegistryValue(
        RegistryRoot root, string key, string valueName, string comparison, string expectedValue)
    {
        _type = SearchConditionType.RegistryValue;
        _path = $@"{FormatRoot(root)}\{key}";
        _value = valueName;
        _comparison = $"{comparison}:{expectedValue}";
        return this;
    }

    internal SearchCondition Build() => new()
    {
        Type = _type,
        Path = _path,
        Value = _value,
        Comparison = _comparison
    };

    private static string FormatRoot(RegistryRoot root) => root switch
    {
        RegistryRoot.LocalMachine => "HKLM",
        RegistryRoot.CurrentUser => "HKCU",
        RegistryRoot.ClassesRoot => "HKCR",
        RegistryRoot.Users => "HKU",
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unsupported registry root")
    };
}
