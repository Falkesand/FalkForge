namespace FalkForge.Studio.Editors.ProductEditor;

public sealed record ProjectTypeItem(string DisplayName, string Value)
{
    public override string ToString() => DisplayName;
}
