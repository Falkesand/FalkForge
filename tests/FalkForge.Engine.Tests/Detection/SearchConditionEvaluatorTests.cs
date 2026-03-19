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
    public void RegistryExists_KeyExists_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().AddKey("HKLM", @"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryExists_KeyMissing_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry();
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryExists_ValueNameExists_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue("HKLM", @"SOFTWARE\App", "Version", "2.0.0");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryExists_ValueNameMissing_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().AddKey("HKLM", @"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_VersionCompare_GreaterOrEqual_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue("HKLM", @"SOFTWARE\App", "Version", "3.1.0");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = ">=:2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryValue_VersionCompare_OlderVersion_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue("HKLM", @"SOFTWARE\App", "Version", "1.0.0");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = ">=:2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_StringEquals_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue("HKLM", @"SOFTWARE\App", "Edition", "Enterprise");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Edition",
            Comparison = "=:Enterprise"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryValue_StringEquals_Mismatch_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue("HKLM", @"SOFTWARE\App", "Edition", "Standard");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Edition",
            Comparison = "=:Enterprise"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_InvalidPath_ReturnsFailure()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry();
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = "INVALID",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }

    [Fact]
    public void ProductSearch_UnsupportedType_ReturnsFailure()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry();
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.ProductSearch,
            Path = "{GUID}"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }
}
