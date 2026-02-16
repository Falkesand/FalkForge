namespace CustomUiSimple.Pages;

using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => "Complete";
    public string Message => "My Application has been successfully installed.";

    public override PageResult OnNext() => PageResult.Finish;
    public override bool CanGoBack => false;
}
