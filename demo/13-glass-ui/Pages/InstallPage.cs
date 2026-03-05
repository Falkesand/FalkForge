using FalkForge;
using FalkForge.Ui;
using GlassUi.Views;

namespace GlassUi.Pages;

public sealed class InstallPage : InstallerPage<InstallView>
{
    public override string Title => "GlassForge";
    public override bool CanGoBack => false;
    public override bool CanGoNext => false;

    public async Task InstallAsync()
    {
        await Engine.PlanAsync(InstallAction.Install);
        await Engine.ApplyAsync();
    }
}