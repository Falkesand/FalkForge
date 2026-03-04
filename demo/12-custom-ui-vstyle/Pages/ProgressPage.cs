using System.Collections.ObjectModel;
using CustomUiVsStyle.Models;
using CustomUiVsStyle.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

namespace CustomUiVsStyle.Pages;

public class ProgressPage : InstallerPage<ProgressView>
{
    private string? _currentOperationOverride;
    private double _overallProgress;

    public override string Title => Localize("Progress.Title");
    public string Header => Localize("Progress.Header");
    public string OverallProgressLabel => Localize("Progress.OverallProgress");

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetField(ref _overallProgress, value);
    }

    public string CurrentOperation
    {
        get => _currentOperationOverride ?? Localize("Progress.Preparing");
        set => SetField(ref _currentOperationOverride, value);
    }

    public ObservableCollection<Workload> InstallingWorkloads { get; } = new();

    public override bool CanGoBack => false;
    public override bool CanGoNext => false;

    public override PageResult OnNext()
    {
        return PageResult.Next;
    }
}