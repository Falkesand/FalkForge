using System.Collections.ObjectModel;
using FalkForge.Studio.Editors.FilesEditor;
using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Navigation;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed class StudioViewModel : ViewModelBase
{
    private readonly StudioProject _project;
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

    public StudioViewModel()
    {
        _project = StudioProjectLoader.NewProject();
        BuildDefaultTree();
    }

    private void BuildDefaultTree()
    {
        TreeNodes.Add(new TreeNodeViewModel("Product", "product") { IsExpanded = true });
        TreeNodes.Add(new TreeNodeViewModel("Files", "files"));
        TreeNodes.Add(new TreeNodeViewModel("Features", "features"));
        TreeNodes.Add(new TreeNodeViewModel("UI & Dialogs", "ui"));
        TreeNodes.Add(new TreeNodeViewModel("Build Settings", "build"));
    }

    public void NavigateTo(string nodeKey)
    {
        OutputText = $"Selected: {nodeKey}";
        CurrentEditor = nodeKey switch
        {
            "product" => new ProductEditorViewModel(_project.Product),
            "files" => new FilesEditorViewModel(_project),
            _ => null
        };
    }
}
