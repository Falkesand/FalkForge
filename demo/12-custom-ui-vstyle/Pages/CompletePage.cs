namespace CustomUiVsStyle.Pages;

using CustomUiVsStyle.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => "Complete";
    public string Message => "FalkForge DevTools Suite has been successfully installed.";
    public string Details => "You can now open FalkForge DevTools from the Start menu.";

    public override PageResult OnNext() => PageResult.Finish;
    public override bool CanGoBack => false;
}
