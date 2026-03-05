using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FeaturesEditor;

public sealed class FeaturesEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private FeatureNodeViewModel? _selectedFeature;

    public ObservableCollection<FeatureNodeViewModel> Features { get; } = [];

    public FeatureNodeViewModel? SelectedFeature
    {
        get => _selectedFeature;
        set => SetProperty(ref _selectedFeature, value);
    }

    public FeaturesEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadFeatures();
    }

    private void LoadFeatures()
    {
        Features.Clear();
        foreach (var feature in _project.Features)
            Features.Add(new FeatureNodeViewModel(feature));
    }

    public void AddFeature(string id, string title)
    {
        var section = new FeatureSection { Id = id, Title = title, IsDefault = true };
        _project.Features.Add(section);
        var vm = new FeatureNodeViewModel(section);
        Features.Add(vm);
        SelectedFeature = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedFeature is null) return;
        if (_project.Features.Count <= 1) return;

        _project.Features.Remove(SelectedFeature.Model);
        Features.Remove(SelectedFeature);
        SelectedFeature = Features.Count > 0 ? Features[0] : null;
    }
}
