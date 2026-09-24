namespace FalkForge.Compiler.Bundle.Compilation;

using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Refuses an embedded runtime binary (the engine stub or the elevation companion) whose Win32
/// <c>ProductVersion</c> resource is older than the compiler that is about to embed it (BDL038). The
/// compiler signs the current envelope version; a binary from an earlier release computes an older
/// signed message and fails every install at the customer with a signature error. Catching it here
/// turns that into a build error with a remedy.
/// When the check cannot decide (no readable version resource, an unparseable version on either side,
/// or an unreadable file) it passes and returns a warning text, so the header-only fixtures the tests
/// embed keep working; whether the text is logged is the caller's choice. Equal versions pass
/// with no warning. Two builds that share one version string are not distinguished.
/// </summary>
internal static class EmbeddedRuntimeVersionCheck
{
    /// <summary>The compiler's own informational version, for example <c>0.5.0-beta.9+sha</c>.</summary>
    internal static string CompilerVersion { get; } =
        typeof(BundleCompiler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;

    /// <summary>The .NET SDK's default assembly version when a project sets none.</summary>
    private static readonly ProductVersionOrder.Parsed SdkDefaultVersion = new(1, 0, 0, string.Empty);

    /// <summary>
    /// Success carries null (decided, fine) or a BDL038 warning text (undecided, passed). Failure is the
    /// BDL038 refusal. <paramref name="role"/> is "engine" or "elevation companion" and appears in both.
    /// </summary>
    internal static Result<string?> Check(string binaryPath, string role, string compilerInformationalVersion)
    {
        if (!ProductVersionOrder.TryParse(compilerInformationalVersion, out _))
            return Warn($"the compiler's own version '{compilerInformationalVersion}' is not SemVer 2, so the {role} at {binaryPath} was not checked");

        string? binaryVersionText;
        try
        {
            binaryVersionText = FileVersionInfo.GetVersionInfo(binaryPath).ProductVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Warn($"the {role} at {binaryPath} could not be read ({ex.Message}), so its version was not checked");
        }

        if (string.IsNullOrEmpty(binaryVersionText))
            return Warn($"the {role} at {binaryPath} has no readable ProductVersion resource, so its version was not checked");

        return CheckVersion(binaryVersionText, compilerInformationalVersion, binaryPath, role);
    }

    /// <summary>
    /// The parse-and-compare half of <see cref="Check"/>, split out so tests can drive it with an
    /// arbitrary <paramref name="binaryVersionText"/> without needing a PE file that carries that exact
    /// Win32 version resource.
    /// </summary>
    internal static Result<string?> CheckVersion(string binaryVersionText, string compilerInformationalVersion, string binaryPath, string role)
    {
        if (!ProductVersionOrder.TryParse(compilerInformationalVersion, out var compiler))
            return Warn($"the compiler's own version '{compilerInformationalVersion}' is not SemVer 2, so the {role} at {binaryPath} was not checked");

        if (!ProductVersionOrder.TryParse(binaryVersionText, out var binary))
            return Warn($"the {role} at {binaryPath} reports ProductVersion '{binaryVersionText}', which is not SemVer 2, so its version was not checked");

        // 1.0.0 with no prerelease (build metadata already stripped by TryParse) is the SDK's default
        // when a project sets no <Version>. FalkForge has never shipped 1.0.0, so no legitimate binary
        // carries it while the compiler is still below 1.0.0. A runtime rebuilt from a
        // FalkForge.Engine.Sources package older than 0.5.0-beta.9 (which set no <Version> in its
        // templates) reports exactly this, and would otherwise read as "newer than the compiler" and
        // pass, failing every install at the customer instead of failing the build.
        if (binary.Major == 1 && binary.Minor == 0 && binary.Patch == 0 && binary.PreRelease.Length == 0
            && ProductVersionOrder.Compare(compiler, SdkDefaultVersion) < 0)
        {
            return Result<string?>.Failure(ErrorKind.BundleError,
                $"BDL038: The resolved {role} reports ProductVersion 1.0.0, the .NET SDK's default version " +
                $"when a project sets none. That is what a runtime rebuilt from a FalkForge.Engine.Sources " +
                $"package older than 0.5.0-beta.9 reports, not a real release, and it would fail to verify " +
                $"the integrity envelope this compiler signs ({compilerInformationalVersion}), so every " +
                $"install of the bundle would be refused. Rebuild from the FalkForge.Engine.Sources package " +
                $"matching {compilerInformationalVersion}, or publish a matching runtime " +
                $"(scripts/publish.ps1). Binary: {binaryPath}");
        }

        if (ProductVersionOrder.Compare(binary, compiler) >= 0)
            return Result<string?>.Success(null);

        return Result<string?>.Failure(ErrorKind.BundleError,
            $"BDL038: The resolved {role} is version {binaryVersionText}, older than this " +
            $"compiler ({compilerInformationalVersion}). It would fail to verify the integrity envelope this " +
            $"compiler signs, so every install of the bundle would be refused. Publish a matching " +
            $"runtime (scripts/publish.ps1) or upgrade the FalkForge.Engine.Runtime.win-x64 package to the " +
            $"compiler's version. Binary: {binaryPath}");
    }

    private static Result<string?> Warn(string detail)
        => Result<string?>.Success($"BDL038: {detail}. A runtime older than the compiler fails every install with a signature error; confirm the binary is at least {CompilerVersion}.");
}
