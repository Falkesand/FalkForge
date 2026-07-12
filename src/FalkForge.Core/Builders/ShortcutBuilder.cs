using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ShortcutBuilder
{
    private readonly List<ShortcutLocation> _locations = [];
    private readonly string _name;
    private readonly Action<ShortcutModel> _onAdd;
    private readonly string _targetFile;
    private string? _arguments;
    private string? _description;
    private bool _hasEmittedLocation;
    private string? _iconFile;
    private int _iconIndex;
    private string? _startMenuSubfolder;
    private string? _workingDirectory;

    /// <summary>
    /// <paramref name="onAdd"/> decouples this builder from any specific owner: <see cref="PackageBuilder"/>
    /// passes <c>AddShortcut</c>, <see cref="FeatureBuilder"/> passes its own feature-scoped list's
    /// <c>Add</c> so shortcuts declared via <c>FeatureBuilder.Shortcut(...)</c> can later be stamped
    /// with the owning feature's id (see <see cref="FeatureBuilder.CollectShortcuts"/>).
    /// </summary>
    internal ShortcutBuilder(string name, string targetFile, Action<ShortcutModel> onAdd)
    {
        _name = name;
        _targetFile = targetFile;
        _onAdd = onAdd;
    }

    public ShortcutBuilder OnDesktop()
    {
        _locations.Add(ShortcutLocation.Desktop);
        _onAdd(BuildCurrent());
        _hasEmittedLocation = true;
        return this;
    }

    public ShortcutBuilder OnStartMenu(string? subfolder = null)
    {
        _locations.Add(ShortcutLocation.StartMenu);
        _startMenuSubfolder = subfolder;
        _onAdd(BuildCurrent());
        _hasEmittedLocation = true;
        return this;
    }

    public ShortcutBuilder OnStartup()
    {
        _locations.Add(ShortcutLocation.Startup);
        _onAdd(BuildCurrent());
        _hasEmittedLocation = true;
        return this;
    }

    public ShortcutBuilder WithArguments(string arguments)
    {
        ThrowIfLocationAlreadyEmitted(nameof(WithArguments));
        _arguments = arguments;
        return this;
    }

    public ShortcutBuilder WithDescription(string description)
    {
        ThrowIfLocationAlreadyEmitted(nameof(WithDescription));
        _description = description;
        return this;
    }

    public ShortcutBuilder WithIcon(string iconFile, int iconIndex = 0)
    {
        ThrowIfLocationAlreadyEmitted(nameof(WithIcon));
        _iconFile = iconFile;
        _iconIndex = iconIndex;
        return this;
    }

    public ShortcutBuilder WithWorkingDirectory(string workingDirectory)
    {
        ThrowIfLocationAlreadyEmitted(nameof(WithWorkingDirectory));
        _workingDirectory = workingDirectory;
        return this;
    }

    /// <summary>
    ///     Each On*() call builds and emits a <see cref="ShortcutModel"/> immediately (there is no
    ///     terminal Build() call in this fluent chain), so a With*() call placed after the last
    ///     On*() call would silently configure a shortcut that is never built. Fail loud instead of
    ///     dropping the configuration: With*() must be called before the first On*() call.
    /// </summary>
    private void ThrowIfLocationAlreadyEmitted(string methodName)
    {
        if (_hasEmittedLocation)
            throw new InvalidOperationException(
                $"ShortcutBuilder.{methodName}() was called after OnDesktop()/OnStartMenu()/OnStartup() " +
                "already emitted a shortcut. Each On*() call builds and adds its shortcut immediately, so " +
                $"a With*() call afterward would be silently dropped. Call {methodName}() before the first " +
                "On*() call in the chain.");
    }

    private ShortcutModel BuildCurrent()
    {
        return new ShortcutModel
        {
            Name = _name,
            TargetFile = _targetFile,
            Locations = [_locations[^1]], // Each call adds one shortcut for that location
            WorkingDirectory = _workingDirectory,
            Arguments = _arguments,
            Description = _description,
            IconFile = _iconFile,
            IconIndex = _iconIndex,
            StartMenuSubfolder = _startMenuSubfolder
        };
    }
}