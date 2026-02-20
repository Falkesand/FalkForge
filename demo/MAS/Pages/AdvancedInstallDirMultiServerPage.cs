using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class AdvancedInstallDirMultiServerPage : MasPageBase<AdvancedInstallDirMultiServerView>
{
    private string _installFolder = @"C:\Program Files (x86)\Aptus\MultiServer";

    public override string Title => "Installation folder for MultiServer";

    public string InstallFolder
    {
        get => _installFolder;
        set => SetField(ref _installFolder, value);
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerInstallFolder", _installFolder);
        return PageResult.GoTo<AdvancedInstallDirMultiServerExPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<DatabaseServerPage>();
}
