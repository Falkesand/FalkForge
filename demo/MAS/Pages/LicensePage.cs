using MAS.Views;

namespace MAS.Pages;

public sealed class LicensePage : MasPageBase<LicenseView>
{
    private bool _accepted;

    public override string Title => Localize("License.Title");
    public override string? Subtitle => Localize("License.Subtitle");
    public override bool ShowPrintButton => true;
    public override bool CanGoNext => _accepted;

    public bool Accepted
    {
        get => _accepted;
        set => SetField(ref _accepted, value, [nameof(CanGoNext)]);
    }

    public string LicenseText => Localize("License.Text");

    public string AcceptCheckboxText => Localize("License.AcceptCheckbox");
}
