namespace CustomUiVsStyle.Pages;

using System.Collections.ObjectModel;
using CustomUiVsStyle.Models;
using CustomUiVsStyle.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class ProgressPage : InstallerPage<ProgressView>
{
    private double _overallProgress;
    private string _currentOperation = "Preparing...";

    public override string Title => "Installing";

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetField(ref _overallProgress, value);
    }

    public string CurrentOperation
    {
        get => _currentOperation;
        set => SetField(ref _currentOperation, value);
    }

    public ObservableCollection<Workload> InstallingWorkloads { get; } = new();

    public override bool CanGoBack => false;
    public override bool CanGoNext => false;

    public override PageResult OnNext() => PageResult.Next;
}
