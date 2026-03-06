using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class IceConfigurationBuilder
{
    private bool _enabled = true;
    private string? _cubFilePath;
    private readonly List<string> _suppressedIces = [];
    private bool _warningsAsErrors;
    private string? _reportPath;

    public IceConfigurationBuilder Disable()
    {
        _enabled = false;
        return this;
    }

    public IceConfigurationBuilder CubFilePath(string path)
    {
        _cubFilePath = path;
        return this;
    }

    public IceConfigurationBuilder Suppress(params string[] iceNames)
    {
        _suppressedIces.AddRange(iceNames);
        return this;
    }

    public IceConfigurationBuilder WarningsAsErrors(bool value = true)
    {
        _warningsAsErrors = value;
        return this;
    }

    public IceConfigurationBuilder ReportPath(string path)
    {
        _reportPath = path;
        return this;
    }

    public IceConfiguration Build() => new()
    {
        Enabled = _enabled,
        CubFilePath = _cubFilePath,
        SuppressedIces = [.. _suppressedIces],
        WarningsAsErrors = _warningsAsErrors,
        ReportPath = _reportPath,
    };
}
