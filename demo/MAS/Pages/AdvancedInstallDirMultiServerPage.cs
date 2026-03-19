using FalkForge.Plugins.FileSystem;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Advanced-only page for choosing the MultiServer installation directory.
/// Matches the WiX BA DestinationFolderPageView for the MultiServer package.
/// </summary>
public sealed class AdvancedInstallDirMultiServerPage : MasPageBase<AdvancedInstallDirMultiServerView>
{
    private string _installFolder = @"C:\Program Files (x86)\Aptus\MultiServer";

    public override string Title => Localize("AdvancedInstallDirMS.Title");

    public string InstallFolderLabel => Localize("AdvancedInstallDirMS.InstallFolderLabel");

    public string InstallFolder
    {
        get => _installFolder;
        set => SetField(ref _installFolder, value);
    }

    public void BrowseFolder()
    {
        var browser = PluginServices.GetService<IFolderBrowser>();
        if (browser is null) return;

        var folder = browser.BrowseForFolder(InstallFolder, Localize("AdvancedInstallDirMS.BrowseDialogTitle"));
        if (folder is not null)
            InstallFolder = folder;
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerInstallFolder", _installFolder);
        return PageResult.GoTo<AdvancedInstallDirMultiServerExPage>();
    }

    public override PageResult OnBack()
    {
        return PageResult.GoTo<DatabaseServerPage>();
    }
}