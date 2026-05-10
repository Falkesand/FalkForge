using System.Collections.ObjectModel;
using System.Windows.Input;
using FalkForge.Studio.Navigation;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

/// <summary>
/// Top-level view-model for FalkForge Studio.
/// Split across partial files by responsibility:
/// <list type="bullet">
///   <item><see cref="StudioViewModel"/> — state, properties, constructor, tree.</item>
///   <item>StudioViewModel.Build.cs — build orchestration.</item>
///   <item>StudioViewModel.Validation.cs — validation, debounce, undo state.</item>
///   <item>StudioViewModel.Navigation.cs — editor navigation, undo/redo, editor factory.</item>
///   <item>StudioViewModel.Project.cs — project lifecycle (new/load/save/import/export).</item>
/// </list>
/// </summary>
public sealed partial class StudioViewModel : ViewModelBase
{
    private StudioProject _project;
    private readonly Dictionary<string, ViewModelBase> _editors = new();
    private readonly UndoManager _undoManager = new();
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _validationDebounce;
    private string? _projectPath;
    private string _baseDirectory = ".";
    private ViewModelBase? _currentEditor;
    private string _outputText = string.Empty;
    private string _title = "FalkForge Studio";
    private bool _isBuildInProgress;
    private string? _buildSummary;
    private bool _buildSucceeded;
    private int _buildProgress;
    private bool _showBuildProgress;

    public ObservableCollection<TreeNodeViewModel> TreeNodes { get; } = [];
    public ObservableCollection<ValidationMessage> ValidationMessages { get; } = [];

    public int ErrorCount => ValidationMessages.Count(m => m.Severity == "Error");
    public int WarningCount => ValidationMessages.Count(m => m.Severity == "Warning");

    public ViewModelBase? CurrentEditor
    {
        get => _currentEditor;
        set => SetProperty(ref _currentEditor, value);
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsBuildInProgress
    {
        get => _isBuildInProgress;
        private set => SetProperty(ref _isBuildInProgress, value);
    }

    public string? BuildSummary
    {
        get => _buildSummary;
        private set
        {
            SetProperty(ref _buildSummary, value);
            OnPropertyChanged(nameof(HasBuildSummary));
        }
    }

    public bool HasBuildSummary => _buildSummary is not null;

    public bool BuildSucceeded
    {
        get => _buildSucceeded;
        private set => SetProperty(ref _buildSucceeded, value);
    }

    public int BuildProgress
    {
        get => _buildProgress;
        private set => SetProperty(ref _buildProgress, value);
    }

    public bool ShowBuildProgress
    {
        get => _showBuildProgress;
        private set => SetProperty(ref _showBuildProgress, value);
    }

    public string? ProjectPath => _projectPath;

    public bool CanUndo => _undoManager.CanUndo;
    public bool CanRedo => _undoManager.CanRedo;

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public StudioViewModel()
        : this(StudioProjectLoader.NewProject(), timeProvider: null)
    {
    }

    public StudioViewModel(StudioProject project)
        : this(project, timeProvider: null)
    {
    }

    public StudioViewModel(StudioProject project, TimeProvider? timeProvider)
    {
        _project = project;
        _timeProvider = timeProvider ?? TimeProvider.System;
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        BuildDefaultTree();
        _undoManager.SaveState(_project);
    }

    private string LocalNowHms()
        => _timeProvider.GetLocalNow().LocalDateTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private void BuildDefaultTree()
    {
        TreeNodes.Add(new TreeNodeViewModel("Product", "product") { IsExpanded = true });
        TreeNodes.Add(new TreeNodeViewModel("Features", "features"));
        TreeNodes.Add(new TreeNodeViewModel("Files", "files"));
        TreeNodes.Add(new TreeNodeViewModel("Registry", "registry"));
        TreeNodes.Add(new TreeNodeViewModel("Services", "services"));
        TreeNodes.Add(new TreeNodeViewModel("Shortcuts", "shortcuts"));
        TreeNodes.Add(new TreeNodeViewModel("Environment", "environment"));
        TreeNodes.Add(new TreeNodeViewModel("Custom Actions", "customActions"));
        var extensions = new TreeNodeViewModel("Extensions", "extensions");
        extensions.Children.Add(new TreeNodeViewModel("SQL", "sql"));
        extensions.Children.Add(new TreeNodeViewModel("Firewall", "firewall"));
        extensions.Children.Add(new TreeNodeViewModel("XML Config", "xmlConfig"));
        extensions.Children.Add(new TreeNodeViewModel("Scheduled Tasks", "scheduledTasks"));
        extensions.Children.Add(new TreeNodeViewModel("Perf Counters", "perfCounters"));
        extensions.Children.Add(new TreeNodeViewModel("ODBC", "odbc"));
        TreeNodes.Add(extensions);
        TreeNodes.Add(new TreeNodeViewModel("UI & Dialogs", "ui"));
        TreeNodes.Add(new TreeNodeViewModel("Dialog Editor", "dialogs"));
        TreeNodes.Add(new TreeNodeViewModel("Build Settings", "build"));

        switch (_project.ProjectType)
        {
            case "bundle":
                TreeNodes.Add(new TreeNodeViewModel("Bundle Settings", "bundleSettings"));
                TreeNodes.Add(new TreeNodeViewModel("Bundle Packages", "bundlePackages"));
                TreeNodes.Add(new TreeNodeViewModel("Bundle Chain", "bundleChain"));
                break;
            case "msix":
                TreeNodes.Add(new TreeNodeViewModel("MSIX Applications", "msixApplications"));
                TreeNodes.Add(new TreeNodeViewModel("Capabilities", "msixCapabilities"));
                break;
        }
    }
}
