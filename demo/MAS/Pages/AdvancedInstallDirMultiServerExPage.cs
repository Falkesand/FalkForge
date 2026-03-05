using FalkForge.Plugins.FileSystem;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class AdvancedInstallDirMultiServerExPage : MasPageBase<AdvancedInstallDirMultiServerExView>
{
    private string _installFolder = @"C:\Program Files (x86)\Aptus\MultiServerEx";

    public override string Title => Localize("AdvancedInstallDirMSEx.Title");

    public string InstallFolderLabel => Localize("AdvancedInstallDirMSEx.InstallFolderLabel");

    public string InstallFolder
    {
        get => _installFolder;
        set => SetField(ref _installFolder, value);
    }

    public void BrowseFolder()
    {
        var browser = PluginServices.GetService<IFolderBrowser>();
        if (browser is null) return;

        var folder = browser.BrowseForFolder(InstallFolder, Localize("AdvancedInstallDirMSEx.BrowseDialogTitle"));
        if (folder is not null)
            InstallFolder = folder;
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerExInstallFolder", _installFolder);
        return PageResult.GoTo<DatabaseConnectionSettingsPage>();
    }

    public override PageResult OnBack()
    {
        return PageResult.GoTo<AdvancedInstallDirMultiServerPage>();
    }
}