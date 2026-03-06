namespace FalkForge.Compiler.Msix.Builders;

public sealed class MsixUpdateSettingsBuilder
{
    private readonly string _appInstallerUri;
    private int _hoursBetweenUpdateChecks = 24;
    private bool _showPrompt;
    private bool _updateBlocksActivation;
    private bool _automaticBackgroundTask;
    private bool _forceUpdateFromAnyVersion;

    public MsixUpdateSettingsBuilder(string appInstallerUri)
    {
        _appInstallerUri = appInstallerUri;
    }

    public MsixUpdateSettingsBuilder HoursBetweenUpdateChecks(int hours)
    {
        _hoursBetweenUpdateChecks = hours;
        return this;
    }

    public MsixUpdateSettingsBuilder ShowPrompt(bool show = true)
    {
        _showPrompt = show;
        return this;
    }

    public MsixUpdateSettingsBuilder UpdateBlocksActivation(bool blocks = true)
    {
        _updateBlocksActivation = blocks;
        return this;
    }

    public MsixUpdateSettingsBuilder AutomaticBackgroundTask(bool automatic = true)
    {
        _automaticBackgroundTask = automatic;
        return this;
    }

    public MsixUpdateSettingsBuilder ForceUpdateFromAnyVersion(bool force = true)
    {
        _forceUpdateFromAnyVersion = force;
        return this;
    }

    internal MsixUpdateSettings Build() => new()
    {
        AppInstallerUri = _appInstallerUri,
        HoursBetweenUpdateChecks = _hoursBetweenUpdateChecks,
        ShowPrompt = _showPrompt,
        UpdateBlocksActivation = _updateBlocksActivation,
        AutomaticBackgroundTask = _automaticBackgroundTask,
        ForceUpdateFromAnyVersion = _forceUpdateFromAnyVersion
    };
}
