using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ProductEditor;

public sealed class ProductEditorViewModel : ViewModelBase
{
    private readonly ProductSection _model;
    private readonly StudioProject _project;

    public event EventHandler? ProjectTypeChanged;

    public ProductEditorViewModel(ProductSection model, StudioProject project)
    {
        _model = model;
        _project = project;
    }

    public string ProjectType
    {
        get => _project.ProjectType;
        set
        {
            if (_project.ProjectType == value) return;
            _project.ProjectType = value;
            OnPropertyChanged();
            ProjectTypeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); }
    }

    public string Manufacturer
    {
        get => _model.Manufacturer;
        set { _model.Manufacturer = value; OnPropertyChanged(); }
    }

    public string Version
    {
        get => _model.Version;
        set { _model.Version = value; OnPropertyChanged(); }
    }

    public string? UpgradeCode
    {
        get => _model.UpgradeCode;
        set { _model.UpgradeCode = value; OnPropertyChanged(); }
    }

    public string Architecture
    {
        get => _model.Architecture;
        set { _model.Architecture = value; OnPropertyChanged(); }
    }

    public string Scope
    {
        get => _model.Scope;
        set { _model.Scope = value; OnPropertyChanged(); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    public string? HelpUrl
    {
        get => _model.HelpUrl;
        set { _model.HelpUrl = value; OnPropertyChanged(); }
    }

    public string? AboutUrl
    {
        get => _model.AboutUrl;
        set { _model.AboutUrl = value; OnPropertyChanged(); }
    }

    public string? UpdateUrl
    {
        get => _model.UpdateUrl;
        set { _model.UpdateUrl = value; OnPropertyChanged(); }
    }

    public string? Phone
    {
        get => _model.Phone;
        set { _model.Phone = value; OnPropertyChanged(); }
    }

    public string? Email
    {
        get => _model.Email;
        set { _model.Email = value; OnPropertyChanged(); }
    }

    public string? Comments
    {
        get => _model.Comments;
        set { _model.Comments = value; OnPropertyChanged(); }
    }

    public string? LicenseFile
    {
        get => _model.LicenseFile;
        set { _model.LicenseFile = value; OnPropertyChanged(); }
    }

    public string[] Architectures { get; } = ["x86", "x64", "arm64"];
    public string[] Scopes { get; } = ["perMachine", "perUser"];
    public ProjectTypeItem[] ProjectTypes { get; } =
    [
        new("MSI Package", "msi"),
        new("EXE Bundle", "bundle"),
        new("MSIX Package", "msix"),
    ];

    public ProjectTypeItem SelectedProjectType
    {
        get => Array.Find(ProjectTypes, p => p.Value == ProjectType) ?? ProjectTypes[0];
        set => ProjectType = value.Value;
    }

    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Product name is required.";
            if (string.IsNullOrWhiteSpace(Manufacturer)) return "Manufacturer is required.";
            if (!System.Version.TryParse(Version, out _)) return "Invalid version format.";
            if (UpgradeCode is not null && !Guid.TryParse(UpgradeCode, out _)) return "Invalid GUID format.";
            return null;
        }
    }
}
