using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.RegistryEditor;

public sealed class RegistryEntryViewModel : ViewModelBase
{
    private readonly RegistryEntrySection _model;

    public RegistryEntryViewModel(RegistryEntrySection model) { _model = model; }

    public RegistryEntrySection Model => _model;

    public string Root { get => _model.Root; set { _model.Root = value; OnPropertyChanged(); } }
    public string Key { get => _model.Key; set { _model.Key = value; OnPropertyChanged(); } }
    public string ValueName { get => _model.ValueName; set { _model.ValueName = value; OnPropertyChanged(); } }
    public string ValueType { get => _model.ValueType; set { _model.ValueType = value; OnPropertyChanged(); } }
    public string Value { get => _model.Value; set { _model.Value = value; OnPropertyChanged(); } }
}
