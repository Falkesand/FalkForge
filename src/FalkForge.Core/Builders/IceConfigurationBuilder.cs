using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class IceConfigurationBuilder
{
    private bool _enabled = true;
    private string? _cubFilePath;
    private readonly List<string> _suppressedIces = [];
    private bool _warningsAsErrors;
    private string? _reportPath;
    private bool _skipWhenCubUnavailable;

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

    /// <summary>
    /// Opts out of strict fail-loud behavior: if darice.cub cannot be found, ICE validation
    /// is silently skipped rather than returning a failure. Use on developer machines or build
    /// environments that intentionally lack the Windows SDK.
    /// </summary>
    public IceConfigurationBuilder SkipWhenCubUnavailable(bool value = true)
    {
        _skipWhenCubUnavailable = value;
        return this;
    }

    public IceConfiguration Build() => new()
    {
        Enabled = _enabled,
        CubFilePath = _cubFilePath,
        SuppressedIces = [.. _suppressedIces],
        WarningsAsErrors = _warningsAsErrors,
        ReportPath = _reportPath,
        SkipWhenCubUnavailable = _skipWhenCubUnavailable,
    };
}
