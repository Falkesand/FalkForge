namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

public sealed class SearchConditionEvaluator(
    IFileSystemProvider fileSystem, IRegistry? registry = null, IEnvironment? environment = null)
{
    /// <summary>
    /// Registry key holding the <c>dotnet</c> install root as its <c>Path</c> value. Present on this
    /// author's dev machine even where the corresponding <c>sharedfx</c> subtree is entirely absent --
    /// see <see cref="SearchConditionType.SharedFrameworkVersion"/> -- so it is a reliable fallback for
    /// locating the shared-framework directory even though it cannot itself express "which shared
    /// frameworks are installed." x64-only, matching every other x64-hardcoded path this evaluator and
    /// <c>BuiltInPrerequisites</c> already use.
    /// </summary>
    private const string SharedHostRegistryKey = @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost";

    public Result<bool> Evaluate(SearchCondition condition)
    {
        return condition.Type switch
        {
            SearchConditionType.FileExists => fileSystem.FileExists(condition.Path),
            SearchConditionType.FileVersion => EvaluateFileVersion(condition),
            SearchConditionType.DirectoryExists => fileSystem.DirectoryExists(condition.Path),
            SearchConditionType.RegistryValue => EvaluateRegistryValue(condition),
            SearchConditionType.SharedFrameworkVersion => EvaluateSharedFrameworkVersion(condition),
            _ => Result<bool>.Failure(ErrorKind.DetectionError, $"Unsupported search condition type: {condition.Type}")
        };
    }

    private Result<bool> EvaluateFileVersion(SearchCondition condition)
    {
        if (!fileSystem.FileExists(condition.Path))
            return false;

        var fileVersion = fileSystem.GetFileVersion(condition.Path);
        if (fileVersion is null || condition.Value is null)
            return false;

        if (!Version.TryParse(condition.Value, out var targetVersion))
            return Result<bool>.Failure(ErrorKind.DetectionError, $"Invalid version format: {condition.Value}");

        return (condition.Comparison ?? "=") switch
        {
            "=" => fileVersion == targetVersion,
            ">" => fileVersion > targetVersion,
            ">=" => fileVersion >= targetVersion,
            "<" => fileVersion < targetVersion,
            "<=" => fileVersion <= targetVersion,
            "<>" => fileVersion != targetVersion,
            _ => Result<bool>.Failure(ErrorKind.DetectionError, $"Unknown comparison: {condition.Comparison}")
        };
    }

    private Result<bool> EvaluateRegistryValue(SearchCondition condition)
    {
        if (registry is null)
            return Result<bool>.Failure(ErrorKind.DetectionError, "Registry provider not available");

        var separatorIndex = condition.Path.IndexOf('\\');
        if (separatorIndex < 0)
            return Result<bool>.Failure(ErrorKind.DetectionError, $"Invalid registry path: {condition.Path}");

        var rootKeyStr = condition.Path[..separatorIndex];
        var subKey = condition.Path[(separatorIndex + 1)..];

        if (!TryParseRegistryRoot(rootKeyStr, out var rootKey))
            return Result<bool>.Failure(ErrorKind.DetectionError, $"Unknown registry root key: {rootKeyStr}");

        var comparison = condition.Comparison ?? "exists";

        if (comparison == "exists")
            return EvaluateRegistryExists(rootKey, subKey, condition.Value);

        return EvaluateRegistryComparison(rootKey, subKey, condition.Value, comparison);
    }

    private static bool TryParseRegistryRoot(string rootKeyStr, out RegistryRoot rootKey)
    {
        rootKey = rootKeyStr switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryRoot.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryRoot.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryRoot.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryRoot.Users,
            _ => default
        };

        return rootKeyStr is "HKLM" or "HKEY_LOCAL_MACHINE"
            or "HKCU" or "HKEY_CURRENT_USER"
            or "HKCR" or "HKEY_CLASSES_ROOT"
            or "HKU" or "HKEY_USERS";
    }

    private Result<bool> EvaluateRegistryExists(RegistryRoot rootKey, string subKey, string? valueName)
    {
        // TryKeyExists / TryGetStringValue (not the bare KeyExists / GetStringValue): this runs on
        // the pre-UI bootstrap path (PreUIPrerequisiteDetector, before the WPF UI exists), so a
        // SecurityException from an ACL-denied key (HKLM\SECURITY, a vendor key with tight ACLs,
        // another user's HKU hive) must surface as a Result Failure instead of escaping uncaught —
        // WindowsRegistry's bare KeyExists/GetStringValue let that exception through unhandled.
        // A Failure here propagates up to PreUIPrerequisiteDetector.IsInstalled, which already
        // treats it the same as "condition false": the prerequisite is reported missing and a
        // (possibly redundant) install is attempted rather than the search silently reporting the
        // prerequisite as present. That mirrors the fail-closed precedent BuiltInVariables set for
        // RebootPending in beta.6 — an unreadable probe is not evidence of the safe answer.
        if (valueName is null)
            return registry!.TryKeyExists(rootKey, subKey);

        // Type-agnostic on purpose: an "exists" check must not care whether the value is REG_SZ,
        // REG_DWORD, or anything else -- GetStringValue/TryGetStringValue only recognize REG_SZ and
        // report SUCCESS with null for every other type, which would misreport a present-but-non-string
        // value as absent.
        return registry!.TryValueExists(rootKey, subKey, valueName);
    }

    private Result<bool> EvaluateRegistryComparison(RegistryRoot rootKey, string subKey, string? valueName, string comparison)
    {
        if (valueName is null)
            return Result<bool>.Failure(ErrorKind.DetectionError, "Value name required for registry comparison");

        // Comparison format: "operator:expectedValue" (e.g., ">=:2.0.0" or "=:Enterprise")
        var colonIndex = comparison.IndexOf(':');
        if (colonIndex < 0)
            return Result<bool>.Failure(ErrorKind.DetectionError, $"Invalid comparison format: {comparison}");

        var op = comparison[..colonIndex];
        var expectedValue = comparison[(colonIndex + 1)..];

        // TryGetStringValue (not the bare GetStringValue) — see EvaluateRegistryExists for why an
        // ACL-denied read must surface as a Result Failure instead of an uncaught exception.
        var actualValueResult = registry!.TryGetStringValue(rootKey, subKey, valueName);
        if (actualValueResult.IsFailure)
            return Result<bool>.Failure(actualValueResult.Error);

        var actualValue = actualValueResult.Value;
        if (actualValue is null)
            return EvaluateRegistryDWordComparison(rootKey, subKey, valueName, op, expectedValue);

        // Try version comparison first
        if (Version.TryParse(actualValue, out var actualVersion) &&
            Version.TryParse(expectedValue, out var expectedVersion))
        {
            return op switch
            {
                "=" => actualVersion == expectedVersion,
                ">" => actualVersion > expectedVersion,
                ">=" => actualVersion >= expectedVersion,
                "<" => actualVersion < expectedVersion,
                "<=" => actualVersion <= expectedVersion,
                "<>" => actualVersion != expectedVersion,
                _ => Result<bool>.Failure(ErrorKind.DetectionError, $"Unknown comparison operator: {op}")
            };
        }

        // Fall back to string comparison
        return op switch
        {
            "=" => string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
            "<>" => !string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
            _ => Result<bool>.Failure(ErrorKind.DetectionError,
                $"Operator '{op}' not supported for non-version string values")
        };
    }

    /// <summary>
    /// Falls back to a numeric (REG_DWORD) read when a string-typed read of the same value came back
    /// null. <see cref="IRegistry.TryGetStringValue"/> reports SUCCESS with null both when the value is
    /// genuinely absent AND when it exists but is a non-string type -- the two cases are indistinguishable
    /// from a string read alone. Built-in prerequisite detection (e.g. <c>BuiltInPrerequisites.NetFx472</c>'s
    /// "Release" value, <c>VCRedist14x64</c>'s "Installed" value) compares against a REG_DWORD, never a
    /// REG_SZ, on every real machine. REG_QWORD, REG_BINARY, and REG_MULTI_SZ are deliberately NOT covered
    /// here -- no built-in prerequisite or documented search condition compares against one, and adding
    /// numeric-vs-binary coercion is a bigger surface than this fix's scope; a value of one of those types
    /// falls through to "absent" exactly as it did before this fix.
    /// </summary>
    private Result<bool> EvaluateRegistryDWordComparison(
        RegistryRoot rootKey, string subKey, string valueName, string op, string expectedValue)
    {
        var dwordResult = registry!.TryGetDWordValue(rootKey, subKey, valueName);
        if (dwordResult.IsFailure)
            return Result<bool>.Failure(dwordResult.Error);

        if (dwordResult.Value is not { } actualDword)
            return false; // genuinely absent (or a type this fallback deliberately does not cover)

        if (!long.TryParse(expectedValue, out var expectedDword))
        {
            return Result<bool>.Failure(ErrorKind.DetectionError,
                $"Registry value '{valueName}' is numeric (DWORD) but comparison value '{expectedValue}' is not a valid integer");
        }

        return op switch
        {
            "=" => actualDword == expectedDword,
            ">" => actualDword > expectedDword,
            ">=" => actualDword >= expectedDword,
            "<" => actualDword < expectedDword,
            "<=" => actualDword <= expectedDword,
            "<>" => actualDword != expectedDword,
            _ => Result<bool>.Failure(ErrorKind.DetectionError, $"Unknown comparison operator: {op}")
        };
    }

    /// <summary>
    /// Evaluates a <see cref="SearchConditionType.SharedFrameworkVersion"/> condition: true when the
    /// <c>&lt;dotnet-root&gt;\shared\&lt;condition.Path&gt;</c> directory contains at least one
    /// version-named subdirectory whose parsed version is &gt;= <c>condition.Value</c>. See the enum
    /// member's xmldoc for why this reads the filesystem instead of the registry.
    /// </summary>
    private Result<bool> EvaluateSharedFrameworkVersion(SearchCondition condition)
    {
        if (condition.Value is null || !Version.TryParse(condition.Value, out var minimumVersion))
            return Result<bool>.Failure(ErrorKind.DetectionError, $"Invalid minimum version: {condition.Value}");

        var dotNetRoot = ResolveDotNetRoot();
        if (dotNetRoot is null)
            return false; // no candidate root at all -- not a read error, just nothing to look under

        var sharedFxDirectory = Path.Combine(dotNetRoot, "shared", condition.Path);

        IReadOnlyList<string> versionDirectories;
        try
        {
            versionDirectories = fileSystem.GetDirectories(sharedFxDirectory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // fileSystem.GetDirectories is contractually never supposed to throw for a read
            // problem (see IFileSystemProvider.GetDirectories's xmldoc) -- but this pre-UI probe
            // runs before any dialog exists to report a crash, so a provider that throws anyway
            // (an ACL-denied shared-framework directory, a TOCTOU race) must degrade instead of
            // escaping uncaught. Same outcome as the "no version directories found" branch below.
            versionDirectories = [];
        }

        foreach (var versionDirectory in versionDirectories)
        {
            var versionName = Path.GetFileName(versionDirectory);

            // A prerelease/build-metadata suffix (e.g. "11.0.0-preview.6.26359.118") never satisfies
            // the condition, regardless of its numeric value -- a preview build of a HIGHER major is
            // still not a safe substitute for a required STABLE runtime (it can be uninstalled or
            // replaced without notice, and carries no compatibility guarantee). Skipping on the raw
            // dash also sidesteps Version.TryParse, which rejects the suffixed string outright.
            if (versionName.Contains('-', StringComparison.Ordinal))
                continue;

            // An unparsable directory name (stray file, unrelated folder) is skipped, not a failure --
            // enumerating a real shared-framework directory must tolerate junk entries.
            if (!Version.TryParse(versionName, out var installedVersion))
                continue;

            if (installedVersion >= minimumVersion)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the <c>dotnet</c> installation root to search under, trying each candidate in order
    /// and falling through on a miss (never on an error the environment surfaces as null/failure) so a
    /// single unreadable source degrades to the next candidate instead of the whole condition:
    /// <list type="number">
    ///   <item><description><c>DOTNET_ROOT</c> environment variable -- the official override for a
    ///   non-default install location, and what the <c>dotnet</c> host itself honors first.</description></item>
    ///   <item><description><c>HKLM\...\sharedhost</c>'s <c>Path</c> value -- present on a real machine
    ///   even when the <c>sharedfx</c> subtree used by the old (removed) detection is entirely absent;
    ///   see this class's <c>SharedHostRegistryKey</c> constant.</description></item>
    ///   <item><description>The default install location, <c>%ProgramFiles%\dotnet</c>, resolved via
    ///   <see cref="IEnvironment.GetFolderPath"/> rather than a hardcoded drive letter.</description></item>
    /// </list>
    /// Returns <see langword="null"/> when none of the above can be determined (no <paramref
    /// name="environment"/> injected and no usable registry value) -- the caller treats that the same
    /// as "directory not found," never as a thrown exception.
    /// </summary>
    private string? ResolveDotNetRoot()
    {
        var fromEnvironmentVariable = environment?.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnvironmentVariable))
            return fromEnvironmentVariable;

        // GetValueOrDefault() folds both "value absent" and "read failed (e.g. access denied)" into
        // null -- deliberately: this is one of three fallback candidates, not the final answer, so an
        // unreadable sharedhost key should fall through to the ProgramFiles default rather than fail
        // the whole condition the way an ACL-denied read fails closed elsewhere in this evaluator.
        var fromSharedHostRegistry = registry?
            .TryGetStringValue(RegistryRoot.LocalMachine, SharedHostRegistryKey, "Path")
            .GetValueOrDefault();
        if (!string.IsNullOrWhiteSpace(fromSharedHostRegistry))
            return fromSharedHostRegistry.TrimEnd('\\');

        if (environment is not null)
            return Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        return null;
    }
}
