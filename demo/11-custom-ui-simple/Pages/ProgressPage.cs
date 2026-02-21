namespace CustomUiSimple.Pages;

using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class ProgressPage : InstallerPage<ProgressView>
{
    private double _progress;

    public override string Title => Localize("Progress.Title");

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string StatusText => Localize("Progress.StatusText");

    public override PageResult OnNext() => PageResult.Install;
    public override bool CanGoBack => false;
}
