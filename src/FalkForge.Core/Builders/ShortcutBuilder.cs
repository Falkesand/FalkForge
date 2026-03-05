using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ShortcutBuilder
{
    private readonly List<ShortcutLocation> _locations = [];
    private readonly string _name;
    private readonly PackageBuilder _parent;
    private readonly string _targetFile;
    private string? _arguments;
    private string? _description;
    private string? _iconFile;
    private int _iconIndex;
    private string? _startMenuSubfolder;
    private string? _workingDirectory;

    internal ShortcutBuilder(string name, string targetFile, PackageBuilder parent)
    {
        _name = name;
        _targetFile = targetFile;
        _parent = parent;
    }

    public ShortcutBuilder OnDesktop()
    {
        _locations.Add(ShortcutLocation.Desktop);
        _parent.AddShortcut(BuildCurrent());
        return this;
    }

    public ShortcutBuilder OnStartMenu(string? subfolder = null)
    {
        _locations.Add(ShortcutLocation.StartMenu);
        _startMenuSubfolder = subfolder;
        _parent.AddShortcut(BuildCurrent());
        return this;
    }

    public ShortcutBuilder OnStartup()
    {
        _locations.Add(ShortcutLocation.Startup);
        _parent.AddShortcut(BuildCurrent());
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