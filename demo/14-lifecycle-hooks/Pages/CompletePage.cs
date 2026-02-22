using FalkForge.Ui;
using FalkForge.Ui.Abstractions;
using LifecycleDemo.Views;

namespace LifecycleDemo.Pages;

public sealed class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => "Complete";
    public override bool CanGoBack => false;
    public override PageResult OnNext() => PageResult.Finish;

    public string Heading => "Installation Complete";
    public string Message => "Contoso DataHub has been successfully installed.\n\nAll lifecycle hooks executed and MSI properties were passed securely.";
}
