namespace CustomUiSimple.Pages;

using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class WelcomePage : InstallerPage<WelcomeView>
{
    public override string Title => "Welcome";

    public string ProductName => "My Application";
    public string Description => "This wizard will install My Application on your computer.\n\nClick Next to continue.";

    public override PageResult OnNext() => PageResult.Next;
    public override bool CanGoBack => false;
}
