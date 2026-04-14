using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class CustomActionBuilder
{
    private readonly string _id;
    private int _baseType;
    private int _flags;
    private string _sourceRef = string.Empty;

    internal CustomActionBuilder(string id)
    {
        _id = id;
    }

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
    ///     Creates a custom action that runs a PowerShell script inline.
    ///     Uses ExeInDir (type 34) targeting powershell.exe located in the SystemFolder
    ///     directory. The Source column on an ExeInDir custom action must be a Directory
    ///     table key (not a formatted expression), so the compiler emits a Directory row
    ///     for SystemFolder under TARGETDIR when any CustomAction references it.
    /// </summary>
    public CustomActionBuilder PowerShellScript(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        _baseType = CustomActionType.ExeInDir;
        _sourceRef = "SystemFolder";
        var escapedScript = script.Replace("\"", "\\\"");
        Target = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{escapedScript}\"";
        return this;
    }

    /// <summary>
    ///     Creates a custom action that runs a PowerShell script from a file.
    ///     Reads the file content and embeds it inline via <see cref="PowerShellScript"/>.
    /// </summary>
    public CustomActionBuilder PowerShellFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PowerShell script not found: {filePath}", filePath);
        return PowerShellScript(File.ReadAllText(filePath));
    }

    /// <summary>
    ///     Marks the custom action as deferred (in-script execution).
    ///     Deferred actions run during the installation script phase.
    /// </summary>
    public CustomActionBuilder Deferred()
    {
        _flags |= CustomActionType.InScript;
        return this;
    }

    /// <summary>
    ///     Marks the custom action as a rollback action.
    ///     Rollback actions run only if the installation fails after reaching
    ///     the point in the script where this action was scheduled.
    ///     Automatically sets the InScript flag.
    /// </summary>
    public CustomActionBuilder Rollback()
    {
        _flags |= CustomActionType.InScript | CustomActionType.Rollback;
        return this;
    }

    /// <summary>
    ///     Marks the custom action as a commit action.
    ///     Commit actions run only after a successful installation.
    ///     Automatically sets the InScript flag.
    /// </summary>
    public CustomActionBuilder Commit()
    {
        _flags |= CustomActionType.InScript | CustomActionType.Commit;
        return this;
    }

    /// <summary>
    ///     Runs the custom action with elevated (SYSTEM) privileges instead of
    ///     impersonating the installing user. Only meaningful for deferred,
    ///     rollback, or commit actions.
    /// </summary>
    public CustomActionBuilder NoImpersonate()
    {
        _flags |= CustomActionType.NoImpersonate;
        return this;
    }

    /// <summary>
    ///     If the custom action fails, the installer continues instead of aborting.
    /// </summary>
    public CustomActionBuilder ContinueOnError()
    {
        _flags |= CustomActionType.Continue;
        return this;
    }

    internal CustomActionModel Build()
    {
        return new CustomActionModel
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
}