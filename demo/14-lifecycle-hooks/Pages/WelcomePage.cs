#pragma warning disable CA1822 // UI-bound properties must remain instance members

using FalkForge.Ui;
using LifecycleDemo.Views;

namespace LifecycleDemo.Pages;

public sealed class WelcomePage : InstallerPage<WelcomeView>
{
    public override string Title => "Welcome";
    public override bool CanGoBack => false;
    public string ProductName => "Contoso DataHub 2026";
    public string Description => "This installer demonstrates engine lifecycle hooks and secure property passing.";
}