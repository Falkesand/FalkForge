namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Specifies where the pre-UI prerequisite payload is sourced from at install time.
/// </summary>
public enum PreUIPayloadMode
{
    /// <summary>
    /// The payload is embedded in the bundle TOC and extracted to
    /// <c>&lt;cacheDir&gt;/preui/&lt;id&gt;</c> before the UI launches.
    /// This is the default.
    /// </summary>
    Embedded = 0,

    /// <summary>
    /// The payload is not embedded. The engine downloads it from
    /// <see cref="PreUIPackageInfo.DownloadUrl"/> at install time, verifying the
    /// SHA-256 hash before execution.
    /// </summary>
    Remote = 1
}
