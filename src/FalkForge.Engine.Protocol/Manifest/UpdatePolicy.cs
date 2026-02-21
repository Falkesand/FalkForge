namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>Update check behavior policy.</summary>
public enum UpdatePolicy
{
    /// <summary>Check for updates and notify the UI. No automatic download.</summary>
    NotifyOnly,

    /// <summary>Download update to cache, then prompt user. (Future: currently behaves as NotifyOnly.)</summary>
    DownloadAndPrompt,

    /// <summary>Download and auto-launch update. (Future: currently behaves as NotifyOnly.)</summary>
    AutoUpdate
}
