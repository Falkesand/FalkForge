namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiRadioButtonModel
{
    public required string Property { get; init; }
    public int Order { get; init; } = 1;
    public required string Value { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    // Settable (not init): DialogSetProducer.Localization.cs resolves !(loc.X) references in
    // place after the template/translator constructs the model, mirroring Control.Text
    // (MsiControlModel.cs).
    public string? Text { get; set; }
}
