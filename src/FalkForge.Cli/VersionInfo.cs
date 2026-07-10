using System.Reflection;

namespace FalkForge.Cli;

/// <summary>
/// Resolves the forge CLI version from this assembly's informational version, which
/// flows from the single-source version (VersionPrefix/VersionSuffix) in the root
/// <c>Directory.Build.props</c>. <c>Program.cs</c> registers this value as the
/// Spectre.Console.Cli application version so <c>forge --version</c> reports it.
/// </summary>
internal static class VersionInfo
{
    /// <summary>
    /// Full informational version, e.g. <c>0.1.0-alpha.1</c>. May carry a
    /// <c>+&lt;metadata&gt;</c> build-metadata suffix (e.g. if Source Link is added
    /// later); consumers tolerate and strip it.
    /// </summary>
    internal static string CliVersion { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(VersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return informational
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
