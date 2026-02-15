namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class MediaTemplateBuilder
{
    private string _cabinetTemplate = "cab{0}.cab";
    private int _maxCabinetSizeMB;
    private int _maxUncompressedMediaSize;
    private FalkInstaller.CompressionLevel _compressionLevel = FalkInstaller.CompressionLevel.High;
    private bool _embedCabinet = true;

    public MediaTemplateBuilder CabinetTemplate(string template)
    {
        _cabinetTemplate = template;
        return this;
    }

    public MediaTemplateBuilder MaxCabinetSizeMB(int sizeMB)
    {
        _maxCabinetSizeMB = sizeMB;
        return this;
    }

    public MediaTemplateBuilder MaxUncompressedMediaSize(int size)
    {
        _maxUncompressedMediaSize = size;
        return this;
    }

    public MediaTemplateBuilder CompressionLevel(FalkInstaller.CompressionLevel level)
    {
        _compressionLevel = level;
        return this;
    }

    public MediaTemplateBuilder EmbedCabinet(bool embed)
    {
        _embedCabinet = embed;
        return this;
    }

    internal MediaTemplateModel Build() => new()
    {
        CabinetTemplate = _cabinetTemplate,
        MaximumCabinetSizeInMB = _maxCabinetSizeMB,
        MaximumUncompressedMediaSize = _maxUncompressedMediaSize,
        CompressionLevel = _compressionLevel,
        EmbedCabinet = _embedCabinet
    };
}
