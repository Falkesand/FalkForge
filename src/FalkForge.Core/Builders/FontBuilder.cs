namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class FontBuilder
{
    private readonly string _fileName;

    internal FontBuilder(string fileName) => _fileName = fileName;

    public string? Title { get; set; }

    internal FontModel Build() => new()
    {
        FileName = _fileName,
        FontTitle = Title
    };
}
