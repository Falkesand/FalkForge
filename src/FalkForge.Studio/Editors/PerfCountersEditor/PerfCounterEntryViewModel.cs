using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.PerfCountersEditor;

public sealed class PerfCounterEntryViewModel : ViewModelBase
{
    private readonly PerfCounterSection _model;

    public PerfCounterEntryViewModel(PerfCounterSection model) { _model = model; }

    public PerfCounterSection Model => _model;

    public string Id { get => _model.Id; set { _model.Id = value; OnPropertyChanged(); } }
    public string CategoryName { get => _model.CategoryName; set { _model.CategoryName = value; OnPropertyChanged(); } }
    public string CounterName { get => _model.CounterName; set { _model.CounterName = value; OnPropertyChanged(); } }
    public string CounterType { get => _model.CounterType; set { _model.CounterType = value; OnPropertyChanged(); } }
    public string? CategoryHelp { get => _model.CategoryHelp; set { _model.CategoryHelp = value; OnPropertyChanged(); } }
    public string? CounterHelp { get => _model.CounterHelp; set { _model.CounterHelp = value; OnPropertyChanged(); } }
}
