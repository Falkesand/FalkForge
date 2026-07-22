namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiDialogModel
{
    public required string Name { get; init; }
    // Settable (not init): DialogSetProducer.Localization.cs resolves !(loc.X) references in
    // place after the template/translator constructs the model, mirroring Control.Text below.
    public string? Title { get; set; }
    public int Width { get; init; } = 370;
    public int Height { get; init; } = 270;
    public int HCentering { get; init; } = 50;
    public int VCentering { get; init; } = 50;
    public MsiDialogAttributes Attributes { get; init; } = MsiDialogAttributes.Visible | MsiDialogAttributes.Modal | MsiDialogAttributes.Minimize | MsiDialogAttributes.TrackDiskSpace;
    public required string FirstControl { get; init; }
    public string? DefaultControl { get; init; }
    public string? CancelControl { get; init; }
    public List<MsiControlModel> Controls { get; init; } = [];
    public List<MsiControlEventModel> Events { get; init; } = [];
    public List<MsiControlConditionModel> Conditions { get; init; } = [];
    public List<MsiEventMappingModel> EventMappings { get; init; } = [];
}
