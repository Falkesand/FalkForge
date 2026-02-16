namespace CustomUiSimple.Pages;

using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class ProgressPage : InstallerPage<ProgressView>
{
    private double _progress;
    private string _statusText = "Preparing installation...";

    public override string Title => "Installing";

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public override PageResult OnNext() => PageResult.Install;
    public override bool CanGoBack => false;
}
