namespace FalkInstaller.Models;

public sealed class MediaTemplateModel
{
    public string CabinetTemplate { get; init; } = "cab{0}.cab";
    public int MaximumCabinetSizeInMB { get; init; }
    public int MaximumUncompressedMediaSize { get; init; }
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.High;
    public bool EmbedCabinet { get; init; } = true;
}
