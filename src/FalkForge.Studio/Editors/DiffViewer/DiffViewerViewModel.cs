using System.Collections.ObjectModel;
using System.Windows.Input;
using FalkForge.Studio.Diff;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.DiffViewer;

public sealed class DiffViewerViewModel : ViewModelBase
{
    private readonly StudioProject _currentProject;
    private StudioProject? _comparisonProject;
    private string? _comparisonFilePath;
    private string _statusText = "No comparison loaded. Click 'Compare With...' to select a project file.";

    public ObservableCollection<DiffEntry> DiffEntries { get; } = [];

    public string? ComparisonFilePath
    {
        get => _comparisonFilePath;
        private set => SetProperty(ref _comparisonFilePath, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand CompareWithCommand { get; }

    public DiffViewerViewModel(StudioProject currentProject)
    {
        _currentProject = currentProject;
        CompareWithCommand = new RelayCommand(CompareWith);
    }

    internal DiffViewerViewModel(StudioProject currentProject, StudioProject comparisonProject)
    {
        _currentProject = currentProject;
        _comparisonProject = comparisonProject;
        CompareWithCommand = new RelayCommand(CompareWith);
        RunDiff();
    }

    public void LoadComparison(string filePath)
    {
        _comparisonProject = StudioProjectLoader.LoadFromFile(filePath);
        ComparisonFilePath = filePath;
        RunDiff();
    }

    private void RunDiff()
    {
        DiffEntries.Clear();

        if (_comparisonProject is null)
            return;

        var entries = ProjectDiffer.Diff(_currentProject, _comparisonProject);
        foreach (var entry in entries)
            DiffEntries.Add(entry);

        StatusText = DiffEntries.Count == 0
            ? "Projects are identical."
            : $"{DiffEntries.Count} difference(s) found.";
    }

    private void CompareWith()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "FalkForge Studio Project (*.ffstudio)|*.ffstudio|All files (*.*)|*.*",
            Title = "Select Project to Compare With"
        };

        if (dialog.ShowDialog() == true)
            LoadComparison(dialog.FileName);
    }
}
