using System.Collections.ObjectModel;
using System.Windows.Input;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.DialogEditor;

public sealed class DialogEditorViewModel : ViewModelBase
{
    private readonly UiSection _model;
    private DialogDefinition? _selectedDialog;
    private DialogControlDefinition? _selectedControl;

    public DialogEditorViewModel(UiSection model)
    {
        _model = model;
        Dialogs = new ObservableCollection<DialogDefinition>(_model.Dialogs);

        AddDialogCommand = new RelayCommand(AddDialog);
        RemoveDialogCommand = new RelayCommand(RemoveDialog, () => SelectedDialog is not null);
        AddControlCommand = new RelayCommand(AddControl, () => SelectedDialog is not null);
        RemoveControlCommand = new RelayCommand(RemoveControl, () => SelectedControl is not null);
        LoadTemplateCommand = new RelayCommand(LoadTemplate);
    }

    public ObservableCollection<DialogDefinition> Dialogs { get; }

    public string[] TemplateNames { get; } = ["Minimal", "InstallDir", "FeatureTree", "Mondo", "Advanced"];

    public string SelectedTemplateName { get; set; } = "Minimal";

    public DialogDefinition? SelectedDialog
    {
        get => _selectedDialog;
        set
        {
            if (SetProperty(ref _selectedDialog, value))
            {
                SelectedControl = null;
                OnPropertyChanged(nameof(Controls));
                OnPropertyChanged(nameof(SelectedDialogName));
                OnPropertyChanged(nameof(SelectedDialogTitle));
                OnPropertyChanged(nameof(SelectedDialogWidth));
                OnPropertyChanged(nameof(SelectedDialogHeight));
            }
        }
    }

    public DialogControlDefinition? SelectedControl
    {
        get => _selectedControl;
        set
        {
            if (SetProperty(ref _selectedControl, value))
            {
                OnPropertyChanged(nameof(SelectedControlType));
                OnPropertyChanged(nameof(SelectedControlX));
                OnPropertyChanged(nameof(SelectedControlY));
                OnPropertyChanged(nameof(SelectedControlWidth));
                OnPropertyChanged(nameof(SelectedControlHeight));
                OnPropertyChanged(nameof(SelectedControlText));
                OnPropertyChanged(nameof(SelectedControlProperty));
                OnPropertyChanged(nameof(SelectedControlCondition));
            }
        }
    }

    public ObservableCollection<DialogControlDefinition>? Controls =>
        _selectedDialog is not null ? new ObservableCollection<DialogControlDefinition>(_selectedDialog.Controls) : null;

    // Dialog properties
    public string? SelectedDialogName
    {
        get => _selectedDialog?.Name;
        set { if (_selectedDialog is not null && value is not null) { _selectedDialog.Name = value; OnPropertyChanged(); } }
    }

    public string? SelectedDialogTitle
    {
        get => _selectedDialog?.Title;
        set { if (_selectedDialog is not null && value is not null) { _selectedDialog.Title = value; OnPropertyChanged(); } }
    }

    public int SelectedDialogWidth
    {
        get => _selectedDialog?.Width ?? 370;
        set { if (_selectedDialog is not null) { _selectedDialog.Width = value; OnPropertyChanged(); } }
    }

    public int SelectedDialogHeight
    {
        get => _selectedDialog?.Height ?? 270;
        set { if (_selectedDialog is not null) { _selectedDialog.Height = value; OnPropertyChanged(); } }
    }

    // Control properties
    public DialogControlType SelectedControlType
    {
        get => _selectedControl?.Type ?? DialogControlType.Text;
        set { if (_selectedControl is not null) { _selectedControl.Type = value; OnPropertyChanged(); } }
    }

    public int SelectedControlX
    {
        get => _selectedControl?.X ?? 0;
        set { if (_selectedControl is not null) { _selectedControl.X = value; OnPropertyChanged(); } }
    }

    public int SelectedControlY
    {
        get => _selectedControl?.Y ?? 0;
        set { if (_selectedControl is not null) { _selectedControl.Y = value; OnPropertyChanged(); } }
    }

    public int SelectedControlWidth
    {
        get => _selectedControl?.Width ?? 0;
        set { if (_selectedControl is not null) { _selectedControl.Width = value; OnPropertyChanged(); } }
    }

    public int SelectedControlHeight
    {
        get => _selectedControl?.Height ?? 0;
        set { if (_selectedControl is not null) { _selectedControl.Height = value; OnPropertyChanged(); } }
    }

    public string? SelectedControlText
    {
        get => _selectedControl?.Text;
        set { if (_selectedControl is not null) { _selectedControl.Text = value; OnPropertyChanged(); } }
    }

    public string? SelectedControlProperty
    {
        get => _selectedControl?.Property;
        set { if (_selectedControl is not null) { _selectedControl.Property = value; OnPropertyChanged(); } }
    }

    public string? SelectedControlCondition
    {
        get => _selectedControl?.Condition;
        set { if (_selectedControl is not null) { _selectedControl.Condition = value; OnPropertyChanged(); } }
    }

    public DialogControlType[] ControlTypes { get; } = Enum.GetValues<DialogControlType>();

    public ICommand AddDialogCommand { get; }
    public ICommand RemoveDialogCommand { get; }
    public ICommand AddControlCommand { get; }
    public ICommand RemoveControlCommand { get; }
    public ICommand LoadTemplateCommand { get; }

    private void AddDialog()
    {
        var dialog = new DialogDefinition { Name = $"Dialog{Dialogs.Count + 1}" };
        Dialogs.Add(dialog);
        _model.Dialogs.Add(dialog);
        SelectedDialog = dialog;
    }

    private void RemoveDialog()
    {
        if (SelectedDialog is null) return;
        var dialog = SelectedDialog;
        _model.Dialogs.Remove(dialog);
        Dialogs.Remove(dialog);
        SelectedDialog = Dialogs.Count > 0 ? Dialogs[0] : null;
    }

    private void AddControl()
    {
        if (SelectedDialog is null) return;
        var control = new DialogControlDefinition
        {
            Type = DialogControlType.Text,
            X = 20,
            Y = 20,
            Width = 100,
            Height = 15,
            Text = "New Control"
        };
        SelectedDialog.Controls.Add(control);
        OnPropertyChanged(nameof(Controls));
        SelectedControl = control;
    }

    private void RemoveControl()
    {
        if (SelectedDialog is null || SelectedControl is null) return;
        var control = SelectedControl;
        SelectedDialog.Controls.Remove(control);
        SelectedControl = SelectedDialog.Controls.Count > 0 ? SelectedDialog.Controls[0] : null;
        OnPropertyChanged(nameof(Controls));
    }

    internal void LoadTemplate()
    {
        var definitions = DialogTemplateProvider.GetDialogs(SelectedTemplateName);
        Dialogs.Clear();
        _model.Dialogs.Clear();
        foreach (var def in definitions)
        {
            Dialogs.Add(def);
            _model.Dialogs.Add(def);
        }
        SelectedDialog = Dialogs.Count > 0 ? Dialogs[0] : null;
    }
}
