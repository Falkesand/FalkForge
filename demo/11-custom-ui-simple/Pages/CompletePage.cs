namespace CustomUiSimple.Pages;

using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => Localize("Complete.Title");
    public string Message => Localize("Complete.Message");

    public override PageResult OnNext() => PageResult.Finish;
    public override bool CanGoBack => false;
}
