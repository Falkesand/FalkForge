namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

public sealed class SearchConditionEvaluator(IFileSystemProvider fileSystem, IRegistry? registry = null)
{
    public Result<bool> Evaluate(SearchCondition condition)
    {
        return condition.Type switch
        {
            SearchConditionType.FileExists => fileSystem.FileExists(condition.Path),
            SearchConditionType.FileVersion => EvaluateFileVersion(condition),
            SearchConditionType.DirectoryExists => fileSystem.DirectoryExists(condition.Path),
            SearchConditionType.RegistryValue => EvaluateRegistryValue(condition),
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

        return registry!.TryGetStringValue(rootKey, subKey, valueName)
            .Map(value => value is not null);
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
            return false;

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
}
