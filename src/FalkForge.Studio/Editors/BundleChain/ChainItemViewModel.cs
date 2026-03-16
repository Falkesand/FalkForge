using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BundleChain;

public sealed class ChainItemViewModel : ViewModelBase
{
    private string _name = "";
    private string _packageType = "MsiPackage";
    private string? _installCondition;
    private bool _isRollbackBoundary;
    private int _displayOrder;
    private bool _vital = true;

    /// <summary>
    /// The backing model when this item represents a package (not a rollback boundary).
    /// </summary>
    public BundlePackageSection? Model { get; }

    public ChainItemViewModel() { }

    public ChainItemViewModel(BundlePackageSection model)
    {
        Model = model;
        _name = model.DisplayName;
        _packageType = model.Type;
        _installCondition = model.InstallCondition;
        _vital = model.Vital;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value) && Model is not null)
                Model.DisplayName = value;
        }
    }

    public string PackageType
    {
        get => _packageType;
        set
        {
            if (SetProperty(ref _packageType, value) && Model is not null)
                Model.Type = value;
        }
    }

    public string? InstallCondition
    {
        get => _installCondition;
        set
        {
            if (SetProperty(ref _installCondition, value) && Model is not null)
                Model.InstallCondition = value;
        }
    }

    public bool IsRollbackBoundary
    {
        get => _isRollbackBoundary;
        set => SetProperty(ref _isRollbackBoundary, value);
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    public bool Vital
    {
        get => _vital;
        set
        {
            if (SetProperty(ref _vital, value) && Model is not null)
                Model.Vital = value;
        }
    }

    public static ChainItemViewModel CreateRollbackBoundary()
    {
        return new ChainItemViewModel
        {
            Name = "Rollback Boundary",
            IsRollbackBoundary = true,
            PackageType = "RollbackBoundary"
        };
    }
}
