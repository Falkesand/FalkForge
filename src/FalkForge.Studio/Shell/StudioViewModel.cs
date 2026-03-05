using System.Collections.ObjectModel;
using System.IO;
using FalkForge.Studio.Editors.BuildSettingsEditor;
using FalkForge.Studio.Editors.FeaturesEditor;
using FalkForge.Studio.Editors.FilesEditor;
using FalkForge.Studio.Editors.RegistryEditor;
using FalkForge.Studio.Editors.ServicesEditor;
using FalkForge.Studio.Editors.ShortcutsEditor;
using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Editors.UiEditor;
using FalkForge.Studio.Navigation;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed class StudioViewModel : ViewModelBase
{
    private StudioProject _project;
    private readonly Dictionary<string, ViewModelBase> _editors = new();
    private string? _projectPath;
    private ViewModelBase? _currentEditor;
    private string _outputText = string.Empty;
    private string _title = "FalkForge Studio";

    public ObservableCollection<TreeNodeViewModel> TreeNodes { get; } = [];

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

    public string? ProjectPath => _projectPath;

    public StudioViewModel()
    {
        _project = StudioProjectLoader.NewProject();
        BuildDefaultTree();
    }

    private void BuildDefaultTree()
    {
        TreeNodes.Add(new TreeNodeViewModel("Product", "product") { IsExpanded = true });
        TreeNodes.Add(new TreeNodeViewModel("Features", "features"));
        TreeNodes.Add(new TreeNodeViewModel("Files", "files"));
        TreeNodes.Add(new TreeNodeViewModel("Registry", "registry"));
        TreeNodes.Add(new TreeNodeViewModel("Services", "services"));
        TreeNodes.Add(new TreeNodeViewModel("Shortcuts", "shortcuts"));
        TreeNodes.Add(new TreeNodeViewModel("UI & Dialogs", "ui"));
        TreeNodes.Add(new TreeNodeViewModel("Build Settings", "build"));
    }

    public void Build(string baseDirectory)
    {
        OutputText = "Building...\n";
        var result = StudioBuildService.Compile(_project, baseDirectory);
        OutputText += result.IsSuccess
            ? $"Build succeeded: {result.Value}\n"
            : $"Build failed: {result.Error.Message}\n";
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
        OutputText = "New project created.";
        Title = "FalkForge Studio - Untitled";
        _projectPath = null;
    }

    public void LoadProject(string path)
    {
        _project = StudioProjectLoader.LoadFromFile(path);
        _editors.Clear();
        CurrentEditor = null;
        OutputText = $"Opened: {path}";
        Title = $"FalkForge Studio - {Path.GetFileName(path)}";
        _projectPath = path;
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

    private ViewModelBase? CreateEditor(string nodeKey) => nodeKey switch
    {
        "product" => new ProductEditorViewModel(_project.Product),
        "files" => new FilesEditorViewModel(_project),
        "features" => new FeaturesEditorViewModel(_project),
        "registry" => new RegistryEditorViewModel(_project),
        "services" => new ServicesEditorViewModel(_project),
        "shortcuts" => new ShortcutsEditorViewModel(_project),
        "ui" => new UiEditorViewModel(_project.Ui),
        "build" => new BuildSettingsEditorViewModel(_project.Build),
        _ => null
    };
}
