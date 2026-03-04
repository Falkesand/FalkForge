using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

namespace CustomUiSimple.Pages;

public class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => Localize("Complete.Title");
    public string Message => Localize("Complete.Message");

    public override bool CanGoBack => false;

    public override PageResult OnNext()
    {
        return PageResult.Finish;
    }
}