#pragma warning disable CA1822 // UI-bound properties must remain instance members

using FalkForge.Ui;
using FalkForge.Ui.Abstractions;
using LifecycleDemo.Views;

namespace LifecycleDemo.Pages;

public sealed class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => "Complete";
    public override bool CanGoBack => false;

    public string Heading => "Installation Complete";

    public string Message =>
        "Contoso DataHub has been successfully installed.\n\nAll lifecycle hooks executed and MSI properties were passed securely.";

    public override PageResult OnNext()
    {
        return PageResult.Finish;
    }
}