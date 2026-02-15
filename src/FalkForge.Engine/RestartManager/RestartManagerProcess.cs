namespace FalkForge.Engine.RestartManager;

/// <summary>
/// Represents a process that is using files targeted by the installation.
/// </summary>
public sealed record RestartManagerProcess(
    int ProcessId,
    string ProcessName,
    string ApplicationName,
    bool CanBeRestarted);
