using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class AdvancedInstallDirMultiServerExPage : MasPageBase<AdvancedInstallDirMultiServerExView>
{
    private string _installFolder = @"C:\Program Files (x86)\Aptus\MultiServerEx";

    public override string Title => "Installation folder for MultiServerEx";

    public string InstallFolder
    {
        get => _installFolder;
        set => SetField(ref _installFolder, value);
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerExInstallFolder", _installFolder);
        return PageResult.GoTo<DatabaseConnectionSettingsPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<AdvancedInstallDirMultiServerPage>();
}
