using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

namespace CustomUiSimple.Pages;

public class WelcomePage : InstallerPage<WelcomeView>
{
    public override string Title => Localize("Welcome.Title");

    public string ProductName => Localize("Welcome.ProductName");
    public string Description => Localize("Welcome.Description");

    public override bool CanGoBack => false;

    public override PageResult OnNext()
    {
        return PageResult.Next;
    }
}