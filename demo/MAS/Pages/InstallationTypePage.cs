using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Lets the user choose between Standard and Advanced installation.
/// Standard: installs all packages with default settings (SQLEXPRESS, default paths).
/// Advanced: unlocks custom install directories, DB connection, ODBC, and service settings.
/// Matches the WiX BA ChoseInstallationTypeView.
/// </summary>
public sealed class InstallationTypePage : MasPageBase<InstallationTypeView>
{
    private bool _isStandard = true;

    public override string Title => Localize("InstallationType.Title");

    // Standard section
    public string StandardRadio => Localize("InstallationType.StandardRadio");
    public string StandardDescription => Localize("InstallationType.StandardDescription");
    public string StandardDetailsHeader => Localize("InstallationType.StandardDetailsHeader");
    public string StandardDetail1 => Localize("InstallationType.StandardDetail1");
    public string StandardDetail1a => Localize("InstallationType.StandardDetail1a");
    public string StandardDetail1b => Localize("InstallationType.StandardDetail1b");
    public string StandardDetail1c => Localize("InstallationType.StandardDetail1c");
    public string StandardDetail1d => Localize("InstallationType.StandardDetail1d");
    public string StandardDetail2 => Localize("InstallationType.StandardDetail2");
    public string StandardDetail2a => Localize("InstallationType.StandardDetail2a");
    public string StandardDetail3 => Localize("InstallationType.StandardDetail3");
    public string StandardDetail4 => Localize("InstallationType.StandardDetail4");

    // Advanced section
    public string AdvancedRadio => Localize("InstallationType.AdvancedRadio");
    public string AdvancedDescription => Localize("InstallationType.AdvancedDescription");
    public string AdvancedDetail1 => Localize("InstallationType.AdvancedDetail1");
    public string AdvancedDetail2 => Localize("InstallationType.AdvancedDetail2");
    public string AdvancedDetail3 => Localize("InstallationType.AdvancedDetail3");
    public string AdvancedDetail4 => Localize("InstallationType.AdvancedDetail4");
    public string AdvancedDetail4a => Localize("InstallationType.AdvancedDetail4a");
    public string AdvancedDetail5 => Localize("InstallationType.AdvancedDetail5");
    public string AdvancedDetail6 => Localize("InstallationType.AdvancedDetail6");
    public string AdvancedDetail6a => Localize("InstallationType.AdvancedDetail6a");

    public bool IsStandard
    {
        get => _isStandard;
        set => SetField(ref _isStandard, value, [nameof(IsAdvanced)]);
    }

    public bool IsAdvanced
    {
        get => !_isStandard;
        set => IsStandard = !value;
    }

    public override PageResult OnNext()
    {
        SharedState.Set("InstallationType", _isStandard ? "Standard" : "Advanced");
        return PageResult.GoTo<DatabaseServerPage>();
    }
}