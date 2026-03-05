using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FilesEditor;

public sealed class FilesEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private FileEntryViewModel? _selectedFile;

    public ObservableCollection<FileEntryViewModel> Files { get; } = [];

    public FileEntryViewModel? SelectedFile
    {
        get => _selectedFile;
        set => SetProperty(ref _selectedFile, value);
    }

    public FilesEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadFiles();
    }

    private void LoadFiles()
    {
        Files.Clear();
        foreach (var feature in _project.Features)
            foreach (var file in feature.Files)
                Files.Add(new FileEntryViewModel(file, feature.Id));
    }

    public void AddFile(string source, string featureId)
    {
        var feature = _project.Features.Find(f => f.Id == featureId);
        if (feature is null) return;

        var entry = new FileEntry { Source = source };
        feature.Files.Add(entry);
        var vm = new FileEntryViewModel(entry, featureId);
        Files.Add(vm);
        SelectedFile = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedFile is null) return;

        var feature = _project.Features.Find(f => f.Id == SelectedFile.FeatureId);
        feature?.Files.Remove(SelectedFile.Model);
        Files.Remove(SelectedFile);
        SelectedFile = null;
    }
}
