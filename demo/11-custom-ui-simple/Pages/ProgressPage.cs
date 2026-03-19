using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

namespace CustomUiSimple.Pages;

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

    public override bool CanGoBack => false;

    public override PageResult OnNext()
    {
        return PageResult.Install;
    }
}