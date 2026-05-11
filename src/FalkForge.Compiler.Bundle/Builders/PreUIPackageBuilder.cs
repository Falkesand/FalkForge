using FalkForge.Compiler.Bundle.Models;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Builders;

/// <summary>
/// Fluent builder for <see cref="PreUIPackageModel"/>.
/// A pre-UI prerequisite is installed by the NativeAOT engine before the managed WPF UI launches,
/// ensuring runtime dependencies (e.g., .NET 10 Desktop Runtime) are available.
/// </summary>
public sealed class PreUIPackageBuilder
{
    private readonly string _sourcePath;
    private string _id;
    private string _displayName = string.Empty;
    private string _arguments = string.Empty;
    private PreUIRebootBehavior _rebootBehavior = PreUIRebootBehavior.IgnoreAndContinue;
    private PreUIPayloadMode _payloadMode = PreUIPayloadMode.Embedded;
    private PreUIRemotePayload? _remotePayload;
    private readonly List<SearchCondition> _searchConditions = [];

    /// <param name="sourcePath">
    /// Path to the installer executable on the build machine.
    /// For remote payloads, this should be an empty string or the filename only;
    /// the actual file will be downloaded at install time.
    /// </param>
    public PreUIPackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        // Default Id: filename without extension
        _id = Path.GetFileNameWithoutExtension(sourcePath);
    }

    /// <summary>Sets the unique identifier for this prerequisite within the bundle.</summary>
    public PreUIPackageBuilder Id(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _id = id;
        return this;
    }

    /// <summary>Sets the human-readable display name shown in the native bootstrap UI.</summary>
    public PreUIPackageBuilder DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _displayName = displayName;
        return this;
    }

    /// <summary>
    /// Sets the command-line arguments passed to the installer process.
    /// Must be non-empty — silent install flags are required (e.g., <c>/quiet /norestart</c>).
    /// </summary>
    public PreUIPackageBuilder Arguments(string arguments)
    {
        _arguments = arguments;
        return this;
    }

    /// <summary>
    /// Sets the reboot handling behaviour for exit code 3010 (reboot required).
    /// Default is <see cref="PreUIRebootBehavior.IgnoreAndContinue"/>.
    /// </summary>
    public PreUIPackageBuilder RebootBehavior(PreUIRebootBehavior behavior)
    {
        _rebootBehavior = behavior;
        return this;
    }

    /// <summary>
    /// Configures this prerequisite as a remote payload (not embedded in the bundle).
    /// The engine downloads and verifies the file at install time.
    /// </summary>
    /// <param name="downloadUrl">HTTPS URL to download from.</param>
    /// <param name="sha256Hash">Expected SHA-256 hash (upper-case hex, no hyphens).</param>
    /// <param name="size">File size in bytes for progress reporting.</param>
    public PreUIPackageBuilder RemotePayload(string downloadUrl, string sha256Hash, long size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hash);
        _remotePayload = new PreUIRemotePayload
        {
            DownloadUrl = downloadUrl,
            Sha256Hash = sha256Hash,
            Size = size
        };
        _payloadMode = PreUIPayloadMode.Remote;
        return this;
    }

    /// <summary>
    /// Adds a detection condition. All conditions must evaluate to true for the prerequisite
    /// to be considered already installed (skipping installation).
    /// At least one condition is required (enforced by BDL028).
    /// </summary>
    public PreUIPackageBuilder SearchCondition(Action<SearchConditionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new SearchConditionBuilder();
        configure(builder);
        _searchConditions.Add(builder.Build());
        return this;
    }

    /// <summary>Builds and returns the immutable <see cref="PreUIPackageModel"/>.</summary>
    public PreUIPackageModel Build()
    {
        return new PreUIPackageModel
        {
            Id = _id,
            DisplayName = _displayName,
            SourcePath = _sourcePath,
            Arguments = _arguments,
            RebootBehavior = _rebootBehavior,
            PayloadMode = _payloadMode,
            RemotePayload = _remotePayload,
            SearchConditions = _searchConditions.AsReadOnly()
        };
    }
}
