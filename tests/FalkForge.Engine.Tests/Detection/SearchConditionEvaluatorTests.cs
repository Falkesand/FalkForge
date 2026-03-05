namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class SearchConditionEvaluatorTests
{
    [Fact]
    public void FileExists_ExistingFile_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe");
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileExists,
            Path = @"C:\Program Files\App\app.exe"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void FileExists_MissingFile_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileExists,
            Path = @"C:\Program Files\App\app.exe"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void FileVersion_MatchingVersion_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe", new Version(2, 1, 0));
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileVersion,
            Path = @"C:\Program Files\App\app.exe",
            Comparison = ">=",
            Value = "2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void FileVersion_OlderVersion_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe", new Version(1, 0, 0));
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileVersion,
            Path = @"C:\Program Files\App\app.exe",
            Comparison = ">=",
            Value = "2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void FileVersion_MissingFile_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileVersion,
            Path = @"C:\Program Files\App\app.exe",
            Comparison = ">=",
            Value = "2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void DirectoryExists_ExistingDir_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\Program Files\App");
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.DirectoryExists,
            Path = @"C:\Program Files\App"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void DirectoryExists_MissingDir_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.DirectoryExists,
            Path = @"C:\Program Files\App"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void UnknownType_ReturnsFailure()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }
}
