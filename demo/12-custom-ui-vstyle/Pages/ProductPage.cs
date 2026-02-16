namespace CustomUiVsStyle.Pages;

using CustomUiVsStyle.Views;
using FalkForge.Engine.Protocol;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class ProductPage : InstallerPage<ProductView>
{
    public override string Title => "FalkForge DevTools Suite";

    public string ProductName => "FalkForge DevTools Suite 2026";
    public string ProductVersion => "17.12.0";
    public string Manufacturer => "FalkForge Technologies";

    public bool IsInstalled => DetectedState == InstallState.Installed;
    public bool IsNotInstalled => DetectedState == InstallState.NotInstalled;

    public override PageResult OnNext() => PageResult.Next;
    public override bool CanGoBack => false;
}
