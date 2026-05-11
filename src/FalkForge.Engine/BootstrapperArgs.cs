namespace FalkForge.Engine;

/// <summary>
/// Command-line flags consumed exclusively by the bootstrapper's pre-UI phase.
/// These flags are internal to the engine process and are never forwarded to the UI child.
/// </summary>
/// <param name="IsBootstrapElevated">
/// <see langword="true"/> when this process was launched by an unelevated parent as the
/// elevated pre-UI prerequisite installer child (<c>--bootstrap-elevated</c> flag present).
/// </param>
/// <param name="CacheDir">
/// Absolute path to the extraction cache directory supplied by the relaunching parent
/// (<c>--cache-dir &lt;path&gt;</c>). <see langword="null"/> when not supplied.
/// </param>
public sealed record BootstrapperArgs(
    bool IsBootstrapElevated,
    string? CacheDir)
{
    /// <summary>A <see cref="BootstrapperArgs"/> representing a normal (non-elevated) launch.</summary>
    public static readonly BootstrapperArgs Default = new(IsBootstrapElevated: false, CacheDir: null);

    /// <summary>
    /// Parses <c>--bootstrap-elevated</c> and <c>--cache-dir &lt;path&gt;</c> from
    /// <paramref name="args"/>. Unknown flags are silently ignored for forward-compatibility.
    /// </summary>
    /// <param name="args">Raw process argument array (e.g. <see cref="System.Environment.GetCommandLineArgs"/>).</param>
    /// <returns>A <see cref="BootstrapperArgs"/> instance; never throws.</returns>
    public static BootstrapperArgs Parse(string[] args)
    {
        bool isBootstrapElevated = false;
        string? cacheDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bootstrap-elevated":
                    isBootstrapElevated = true;
                    break;

                case "--cache-dir":
                    if (i + 1 < args.Length)
                        cacheDir = args[++i];
                    break;
            }
        }

        return new BootstrapperArgs(isBootstrapElevated, cacheDir);
    }
}
