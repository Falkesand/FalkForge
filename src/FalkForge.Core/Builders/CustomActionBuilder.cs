namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class CustomActionBuilder
{
    private readonly string _id;
    private int _baseType;
    private int _flags;
    private string _sourceRef = string.Empty;

    internal CustomActionBuilder(string id) => _id = id;

    public string? Target { get; set; }
    public string? Condition { get; set; }
    public int? Sequence { get; set; }
    public string? After { get; set; }
    public string? Before { get; set; }

    public CustomActionBuilder DllFromBinary(string binaryName, string entryPoint)
    {
        _baseType = CustomActionType.DllFromBinary;
        _sourceRef = binaryName;
        Target = entryPoint;
        return this;
    }

    public CustomActionBuilder ExeFromBinary(string binaryName)
    {
        _baseType = CustomActionType.ExeFromBinary;
        _sourceRef = binaryName;
        return this;
    }

    public CustomActionBuilder SetProperty(string propertyName, string value)
    {
        _baseType = CustomActionType.SetProperty;
        _sourceRef = propertyName;
        Target = value;
        return this;
    }

    /// <summary>
    /// Marks the custom action as deferred (in-script execution).
    /// Deferred actions run during the installation script phase.
    /// </summary>
    public CustomActionBuilder Deferred()
    {
        _flags |= CustomActionType.InScript;
        return this;
    }

    /// <summary>
    /// Marks the custom action as a rollback action.
    /// Rollback actions run only if the installation fails after reaching
    /// the point in the script where this action was scheduled.
    /// Automatically sets the InScript flag.
    /// </summary>
    public CustomActionBuilder Rollback()
    {
        _flags |= CustomActionType.InScript | CustomActionType.Rollback;
        return this;
    }

    /// <summary>
    /// Marks the custom action as a commit action.
    /// Commit actions run only after a successful installation.
    /// Automatically sets the InScript flag.
    /// </summary>
    public CustomActionBuilder Commit()
    {
        _flags |= CustomActionType.InScript | CustomActionType.Commit;
        return this;
    }

    /// <summary>
    /// Runs the custom action with elevated (SYSTEM) privileges instead of
    /// impersonating the installing user. Only meaningful for deferred,
    /// rollback, or commit actions.
    /// </summary>
    public CustomActionBuilder NoImpersonate()
    {
        _flags |= CustomActionType.NoImpersonate;
        return this;
    }

    /// <summary>
    /// If the custom action fails, the installer continues instead of aborting.
    /// </summary>
    public CustomActionBuilder ContinueOnError()
    {
        _flags |= CustomActionType.Continue;
        return this;
    }

    internal CustomActionModel Build() => new()
    {
        Id = _id,
        Type = _baseType | _flags,
        SourceRef = _sourceRef,
        Target = Target,
        Condition = Condition,
        Sequence = Sequence,
        After = After,
        Before = Before
    };
}
