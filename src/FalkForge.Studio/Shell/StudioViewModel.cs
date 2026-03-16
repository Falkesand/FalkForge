using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using FalkForge;
using FalkForge.Studio.Editors.BuildSettingsEditor;
using FalkForge.Studio.Editors.BundlePackagesEditor;
using FalkForge.Studio.Editors.BundleSettingsEditor;
using FalkForge.Studio.Editors.FeaturesEditor;
using FalkForge.Studio.Editors.FilesEditor;
using FalkForge.Studio.Editors.RegistryEditor;
using FalkForge.Studio.Editors.ServicesEditor;
using FalkForge.Studio.Editors.ShortcutsEditor;
using FalkForge.Studio.Editors.EnvironmentEditor;
using FalkForge.Studio.Editors.CustomActionsEditor;
using FalkForge.Studio.Editors.FirewallEditor;
using FalkForge.Studio.Editors.OdbcEditor;
using FalkForge.Studio.Editors.PerfCountersEditor;
using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Editors.ScheduledTasksEditor;
using FalkForge.Studio.Editors.SqlEditor;
using FalkForge.Studio.Editors.UiEditor;
using FalkForge.Studio.Editors.XmlConfigEditor;
using FalkForge.Studio.Navigation;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed class StudioViewModel : ViewModelBase
{
    private StudioProject _project;
    private readonly Dictionary<string, ViewModelBase> _editors = new();
    private readonly UndoManager _undoManager = new();
    private string? _projectPath;
    private ViewModelBase? _currentEditor;
    private string _outputText = string.Empty;
    private string _title = "FalkForge Studio";
    private bool _isBuildInProgress;

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

    public string? ProjectPath => _projectPath;

    public bool CanUndo => _undoManager.CanUndo;
    public bool CanRedo => _undoManager.CanRedo;

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public StudioViewModel()
    {
        _project = StudioProjectLoader.NewProject();
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        BuildDefaultTree();
        _undoManager.SaveState(_project);
    }

    public StudioViewModel(StudioProject project)
    {
        _project = project;
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        BuildDefaultTree();
        _undoManager.SaveState(_project);
    }

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
        TreeNodes.Add(new TreeNodeViewModel("Build Settings", "build"));

        switch (_project.ProjectType)
        {
            case "bundle":
                TreeNodes.Add(new TreeNodeViewModel("Bundle Settings", "bundleSettings"));
                TreeNodes.Add(new TreeNodeViewModel("Bundle Packages", "bundlePackages"));
                break;
            case "msix":
                TreeNodes.Add(new TreeNodeViewModel("MSIX Applications", "msixApplications"));
                TreeNodes.Add(new TreeNodeViewModel("Capabilities", "msixCapabilities"));
                break;
        }
    }

    public async Task BuildAsync(string baseDirectory)
    {
        IsBuildInProgress = true;
        OutputText = $"Build started at {DateTime.Now:HH:mm:ss}\n";

        try
        {
            var result = await Task.Run(() => StudioBuildService.Compile(_project, baseDirectory));

            if (result.IsSuccess)
            {
                OutputText += $"Build succeeded: {result.Value}\n";
                OutputText += $"Completed at {DateTime.Now:HH:mm:ss}\n";
            }
            else
            {
                OutputText += $"Build failed: {result.Error.Message}\n";
                OutputText += $"Failed at {DateTime.Now:HH:mm:ss}\n";
            }
        }
        catch (Exception ex)
        {
            OutputText += $"Build error: {ex.Message}\n";
        }
        finally
        {
            IsBuildInProgress = false;
        }

        RunValidation(baseDirectory);
    }

    public void RunValidation(string baseDirectory = ".")
    {
        ValidationMessages.Clear();

        if (string.IsNullOrWhiteSpace(_project.Product.Name))
            ValidationMessages.Add(new ValidationMessage { Code = "STU001", Severity = "Error", Message = "Product name is empty.", EditorKey = "product" });

        if (string.IsNullOrWhiteSpace(_project.Product.Manufacturer))
            ValidationMessages.Add(new ValidationMessage { Code = "STU002", Severity = "Error", Message = "Manufacturer is empty.", EditorKey = "product" });

        if (!Version.TryParse(_project.Product.Version, out _))
            ValidationMessages.Add(new ValidationMessage { Code = "STU003", Severity = "Error", Message = $"Version '{_project.Product.Version}' is not valid.", EditorKey = "product" });

        if (_project.Features.Count == 0)
            ValidationMessages.Add(new ValidationMessage { Code = "STU004", Severity = "Error", Message = "No features defined.", EditorKey = "features" });

        foreach (var feature in _project.Features)
            CheckFeatureFiles(feature);

        if (!string.IsNullOrWhiteSpace(_project.Product.UpgradeCode) && !Guid.TryParse(_project.Product.UpgradeCode, out _))
            ValidationMessages.Add(new ValidationMessage { Code = "STU006", Severity = "Error", Message = $"Upgrade code '{_project.Product.UpgradeCode}' is not a valid GUID.", EditorKey = "product" });

        var modelResult = StudioBuildService.BuildModel(_project, baseDirectory);
        if (modelResult.IsFailure)
        {
            var alreadyCovered = ValidationMessages.Any(m => modelResult.Error.Message.Contains(m.Message, StringComparison.OrdinalIgnoreCase));
            if (!alreadyCovered)
                ValidationMessages.Add(new ValidationMessage { Code = "STU099", Severity = "Error", Message = modelResult.Error.Message });
        }

        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
    }

    private void CheckFeatureFiles(Project.FeatureSection feature)
    {
        if (feature.Files.Count == 0 && (feature.Features is null || feature.Features.Count == 0))
            ValidationMessages.Add(new ValidationMessage { Code = "STU005", Severity = "Warning", Message = $"Feature '{feature.Id}' has no files.", EditorKey = "features" });

        if (feature.Features is not null)
        {
            foreach (var sub in feature.Features)
                CheckFeatureFiles(sub);
        }
    }

    public void NavigateTo(string nodeKey)
    {
        if (!_editors.TryGetValue(nodeKey, out var editor))
        {
            editor = CreateEditor(nodeKey);
            if (editor is not null)
                _editors[nodeKey] = editor;
        }
        CurrentEditor = editor;
    }

    public void NewProject()
    {
        _project = StudioProjectLoader.NewProject();
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = "New project created.";
        Title = "FalkForge Studio - Untitled";
        _projectPath = null;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void NewProject(ProjectTemplate template)
    {
        _project = template.Create();
        _projectPath = null;
        Title = $"FalkForge Studio - {_project.Product.Name}";
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"New project created from template: {template.Name}";
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void LoadProject(string path)
    {
        _project = StudioProjectLoader.LoadFromFile(path);
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"Opened: {path}";
        Title = $"FalkForge Studio - {Path.GetFileName(path)}";
        _projectPath = path;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void ImportMsi(string msiPath)
    {
        var result = Import.MsiImporter.Import(msiPath);
        if (result.IsFailure)
        {
            OutputText = $"Import failed: {result.Error.Message}";
            return;
        }

        _project = result.Value;
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"Imported MSI: {Path.GetFileName(msiPath)}";
        Title = $"FalkForge Studio - {Path.GetFileName(msiPath)} (imported)";
        _projectPath = null;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void ImportWix(string wxsPath)
    {
        var result = Import.WixImporter.Import(wxsPath);
        if (result.IsFailure)
        {
            OutputText = $"Import failed: {result.Error.Message}";
            return;
        }

        _project = result.Value;
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"Imported WiX source: {Path.GetFileName(wxsPath)}";
        Title = $"FalkForge Studio - {Path.GetFileName(wxsPath)} (imported)";
        _projectPath = null;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void SaveProject(string? path = null)
    {
        path ??= _projectPath;
        if (path is null) return;
        StudioProjectLoader.SaveToFile(_project, path);
        OutputText = $"Saved: {path}";
        Title = $"FalkForge Studio - {Path.GetFileName(path)}";
        _projectPath = path;
    }

    public Result<string> ExportCSharpScript()
    {
        return Export.CSharpExporter.Export(_project);
    }

    public void SaveUndoState()
    {
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Undo()
    {
        var restored = _undoManager.Undo(_project);
        if (restored is null) return;

        _project = restored;
        RefreshCurrentEditor();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Redo()
    {
        var restored = _undoManager.Redo(_project);
        if (restored is null) return;

        _project = restored;
        RefreshCurrentEditor();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RefreshCurrentEditor()
    {
        var currentKey = _editors.FirstOrDefault(kv => kv.Value == CurrentEditor).Key;
        _editors.Clear();
        if (currentKey is not null)
            NavigateTo(currentKey);
        else
            CurrentEditor = null;
    }

    private ViewModelBase? CreateEditor(string nodeKey) => nodeKey switch
    {
        "product" => CreateProductEditor(),
        "files" => new FilesEditorViewModel(_project),
        "features" => new FeaturesEditorViewModel(_project),
        "registry" => new RegistryEditorViewModel(_project),
        "services" => new ServicesEditorViewModel(_project),
        "shortcuts" => new ShortcutsEditorViewModel(_project),
        "environment" => new EnvironmentEditorViewModel(_project),
        "customActions" => new CustomActionsEditorViewModel(_project),
        "sql" => new SqlEditorViewModel(_project),
        "firewall" => new FirewallEditorViewModel(_project),
        "xmlConfig" => new XmlConfigEditorViewModel(_project),
        "scheduledTasks" => new ScheduledTasksEditorViewModel(_project),
        "perfCounters" => new PerfCountersEditorViewModel(_project),
        "odbc" => new OdbcEditorViewModel(_project),
        "ui" => new UiEditorViewModel(_project.Ui),
        "build" => new BuildSettingsEditorViewModel(_project.Build),
        "bundleSettings" => new BundleSettingsEditorViewModel(_project.BundleSettings ??= new()),
        "bundlePackages" => new BundlePackagesEditorViewModel(_project),
        _ => null
    };

    private ProductEditorViewModel CreateProductEditor()
    {
        var vm = new ProductEditorViewModel(_project.Product, _project);
        vm.ProjectTypeChanged += OnProjectTypeChanged;
        return vm;
    }

    private void OnProjectTypeChanged(object? sender, EventArgs e)
    {
        var currentKey = _editors.FirstOrDefault(kv => kv.Value == CurrentEditor).Key;
        TreeNodes.Clear();
        BuildDefaultTree();

        if (currentKey is not null && _editors.ContainsKey(currentKey) && TreeNodeExists(currentKey))
        {
            CurrentEditor = _editors[currentKey];
        }
        else if (currentKey != "product")
        {
            // Current editor's node was removed; fall back to product
            NavigateTo("product");
        }
    }

    private bool TreeNodeExists(string key)
    {
        return TreeNodes.Any(n => n.NodeKey == key || n.Children.Any(c => c.NodeKey == key));
    }
}
