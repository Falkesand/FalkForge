using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class FontBuilder
{
    private readonly string _fileName;

    internal FontBuilder(string fileName)
    {
        _fileName = fileName;
    }

    public string? Title { get; set; }

    internal FontModel Build()
    {
        return new FontModel
        {
            FileName = _fileName,
            FontTitle = Title
        };
    }
}