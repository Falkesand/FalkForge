using CustomUiVsStyle.Views;
using FalkForge.Engine.Protocol;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

#pragma warning disable CA1822 // UI-bound properties must remain instance members

namespace CustomUiVsStyle.Pages;

public class ProductPage : InstallerPage<ProductView>
{
    public override string Title => Localize("Product.Title");

    public string ProductName => Localize("Product.ProductName");
    public string Description => Localize("Product.Description");
    public string IncludedWorkloadsLabel => Localize("Product.IncludedWorkloads");
    public string ProductVersion => "17.12.0";
    public string Manufacturer => "FalkForge Technologies";

    public bool IsInstalled => DetectedState == InstallState.Installed;
    public bool IsNotInstalled => DetectedState == InstallState.NotInstalled;

    public override bool CanGoBack => false;

    public override PageResult OnNext()
    {
        return PageResult.Next;
    }
}