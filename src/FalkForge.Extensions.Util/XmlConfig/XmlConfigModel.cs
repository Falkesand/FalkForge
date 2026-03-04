namespace FalkForge.Extensions.Util.XmlConfig;

public sealed class XmlConfigModel
{
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public required string XPath { get; init; }
    public required XmlConfigAction Action { get; init; }
    public string? ElementName { get; init; }
    public string? AttributeName { get; init; }
    public string? Value { get; init; }
    public int Sequence { get; init; }
    public string? ComponentRef { get; init; }
}