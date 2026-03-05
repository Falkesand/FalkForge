namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol.Manifest;

public sealed class SearchConditionEvaluator(IFileSystemProvider fileSystem)
{
    public Result<bool> Evaluate(SearchCondition condition)
    {
        return condition.Type switch
        {
            SearchConditionType.FileExists => fileSystem.FileExists(condition.Path),
            SearchConditionType.FileVersion => EvaluateFileVersion(condition),
            SearchConditionType.DirectoryExists => fileSystem.DirectoryExists(condition.Path),
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
}
