using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.XmlConfigEditor;

public sealed class XmlConfigEntryViewModel : ViewModelBase
{
    private readonly XmlConfigSection _model;

    public XmlConfigEntryViewModel(XmlConfigSection model) { _model = model; }

    public XmlConfigSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string FilePath { get => _model.FilePath; set { _model.FilePath = value; OnPropertyChanged(); } }
    public string XPath { get => _model.XPath; set { _model.XPath = value; OnPropertyChanged(); } }
    public string Action { get => _model.Action; set { _model.Action = value; OnPropertyChanged(); } }
    public string? ElementName { get => _model.ElementName; set { _model.ElementName = value; OnPropertyChanged(); } }
    public string? AttributeName { get => _model.AttributeName; set { _model.AttributeName = value; OnPropertyChanged(); } }
    public string? Value { get => _model.Value; set { _model.Value = value; OnPropertyChanged(); } }
    public int Sequence { get => _model.Sequence; set { _model.Sequence = value; OnPropertyChanged(); } }
}
