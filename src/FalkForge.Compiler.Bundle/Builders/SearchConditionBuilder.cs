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

    internal SearchCondition Build() => new()
    {
        Type = _type,
        Path = _path,
        Value = _value,
        Comparison = _comparison
    };
}
