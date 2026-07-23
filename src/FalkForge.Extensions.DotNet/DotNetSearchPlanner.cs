using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Extensions.DotNet;

/// <summary>
///     Plans the MSI-native detection of every authored <see cref="DotNetCoreSearchModel"/>: a
///     <c>DrLocator</c> + <c>Signature</c> (file-version) search over the shared-framework directory
///     under Program Files, bound to the author's own property name via <c>AppSearch</c>. Unlike the
///     Dependency extension's version-RANGE consumer check (which needs a JScript custom action because
///     MSI condition operators compare lexicographically), a .NET runtime search is a single min-version
///     file search that the real MSI engine's built-in <c>AppSearch</c> standard action performs natively
///     — no custom action required.
///     <para>
///     Registry-based detection (<see cref="DotNetDetector"/>'s <c>sharedfx</c> keys, where versions are
///     SUBKEY names) has no MSI-native equivalent: <c>RegLocator</c> can search hive+key+value, not
///     enumerate subkey names as versions. The filesystem sentinel-file search below is what MSI's
///     <c>DrLocator</c>/<c>Signature</c> machinery can natively express, mirroring
///     <see cref="DotNetDetector.DetectHostfxrFromFileSystem"/>'s directory layout.
///     </para>
/// </summary>
internal static class DotNetSearchPlanner
{
    internal static IReadOnlyList<DotNetSearchPlan> Plan(IReadOnlyList<DotNetCoreSearchModel> searches)
    {
        if (searches.Count == 0)
            return [];

        var plans = new List<DotNetSearchPlan>(searches.Count);
        foreach (var search in searches)
        {
            var (sharedFxDirectory, fileName) = SharedFrameworkInfo(search.RuntimeType);
            var root = ProgramFilesRoot(search.Platform);
            var path = $@"{root}dotnet\shared\{sharedFxDirectory}";

            plans.Add(new DotNetSearchPlan(
                search.VariableName,
                "FalkNetSig" + Suffix(search),
                path,
                fileName,
                FormatVersion(search.MinimumVersion),
                search.Message));
        }

        return plans;
    }

    /// <summary>
    ///     Stable 8-hex-char content hash of the variable name + runtime type + platform, salting the
    ///     synthetic <c>Signature</c> key so two searches (even across separate <see cref="DotNetExtension"/>
    ///     instances in one package) never collide, mirroring
    ///     <c>DependencyVersionCheckPlanner.Suffix</c>.
    /// </summary>
    private static string Suffix(DotNetCoreSearchModel search)
    {
        var material = $"{search.VariableName} {search.RuntimeType} {search.Platform}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return Convert.ToHexStringLower(hash[..4]);
    }

    /// <summary>
    ///     The shared-framework subdirectory name (under <c>dotnet\shared\</c>) and the sentinel DLL whose
    ///     file version stands in for the shared framework's version. <see cref="DotNetRuntimeType.Sdk"/>
    ///     has no shared-framework directory (the SDK is versioned via <c>dotnet\sdk\{version}\</c>, a
    ///     different layout) and is rejected at author time by <see cref="DotNetSearchValidator"/>
    ///     (NET004) before a plan is ever built for it — this default arm is an unreachable defensive
    ///     guard, not a normal Result-style failure path.
    /// </summary>
    private static (string Directory, string FileName) SharedFrameworkInfo(DotNetRuntimeType runtimeType)
        => runtimeType switch
        {
            DotNetRuntimeType.Runtime => ("Microsoft.NETCore.App", "coreclr.dll"),
            DotNetRuntimeType.AspNetCore => ("Microsoft.AspNetCore.App", "Microsoft.AspNetCore.dll"),
            DotNetRuntimeType.WindowsDesktop => ("Microsoft.WindowsDesktop.App", "PresentationCore.dll"),
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeType), runtimeType,
                "Sdk (and any future runtime type) has no MSI-native shared-framework sentinel; " +
                "DotNetSearchValidator (NET004) must reject it before a plan reaches this point."),
        };

    /// <summary>
    ///     MSI Formatted-text reference to the platform-appropriate Program Files root. X64 and Arm64
    ///     .NET installers both target the 64-bit Program Files tree; only X86 uses the WOW64 tree.
    /// </summary>
    private static string ProgramFilesRoot(DotNetPlatform platform)
        => platform == DotNetPlatform.X86 ? "[ProgramFilesFolder]" : "[ProgramFiles64Folder]";

    /// <summary>
    ///     Normalizes a <see cref="Version"/> to a full four-part string (missing Build/Revision become 0)
    ///     so the <c>Signature.MinVersion</c> file-version comparison operand is unambiguous, mirroring
    ///     <c>DependencyVersionCheckPlanner.FormatVersion</c>.
    /// </summary>
    private static string FormatVersion(Version version)
        => new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0)).ToString(4);
}
