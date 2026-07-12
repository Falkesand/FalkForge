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
        return this;
    }

    public ShortcutBuilder OnStartMenu(string? subfolder = null)
    {
        _locations.Add(ShortcutLocation.StartMenu);
        _startMenuSubfolder = subfolder;
        _onAdd(BuildCurrent());
        return this;
    }

    public ShortcutBuilder OnStartup()
    {
        _locations.Add(ShortcutLocation.Startup);
        _onAdd(BuildCurrent());
        return this;
    }

    public ShortcutBuilder WithArguments(string arguments)
    {
        _arguments = arguments;
        return this;
    }

    public ShortcutBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ShortcutBuilder WithIcon(string iconFile, int iconIndex = 0)
    {
        _iconFile = iconFile;
        _iconIndex = iconIndex;
        return this;
    }

    public ShortcutBuilder WithWorkingDirectory(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        return this;
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