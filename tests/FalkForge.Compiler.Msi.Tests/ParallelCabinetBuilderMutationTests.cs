using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class ParallelCabinetBuilderMutationTests
{
    private static ResolvedFile MakeFile(string name) => new()
    {
        SourcePath = $@"C:\fake\{name}",
        TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
        FileName = name,
        FileSize = 1024,
        ComponentId = $"C_{Path.GetFileNameWithoutExtension(name)}",
        FileId = $"F_{Path.GetFileNameWithoutExtension(name)}",
    };

    private static CabinetWorkItem MakeWorkItem(string cabName, int fileCount)
    {
        var files = Enumerable.Range(1, fileCount)
            .Select(i => MakeFile($"{cabName}_file{i}.dll"))
            .ToList();
        return new CabinetWorkItem(cabName, files, CompressionLevel.High);
    }

    [Fact]
    public void Constructor_NullBuildFunc_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ParallelCabinetBuilder(null!));

        Assert.Equal("buildFunc", ex.ParamName);
    }

    [Fact]
    public async Task BuildAsync_EmptyWorkItems_ReturnsSuccessWithEmptyArray()
    {
        var builder = new ParallelCabinetBuilder((_, _) =>
            throw new InvalidOperationException("Should not be called"));

        var result = await builder.BuildAsync([], maxDegreeOfParallelism: 4, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task BuildAsync_SingleItem_UsesSinglePath_NotParallel()
    {
        var callCount = 0;
        var builder = new ParallelCabinetBuilder((workItem, ct) =>
        {
            Interlocked.Increment(ref callCount);
            return new CabinetBuildResult(
                workItem.CabinetName,
                $@"C:\out\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                workItem.FileEntries.Count * 100L);
        });

        var workItems = new[] { MakeWorkItem("Single", 3) };
        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 8, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, callCount);
        Assert.Single(result.Value);
        Assert.Equal("Single", result.Value[0].CabinetName);
        Assert.Equal(@"C:\out\Single.cab", result.Value[0].OutputPath);
        Assert.Equal(3, result.Value[0].FileCount);
        Assert.Equal(300L, result.Value[0].CompressedSize);
    }

    [Fact]
    public async Task BuildAsync_SingleItem_Failure_ReturnsOriginalError()
    {
        var builder = new ParallelCabinetBuilder((_, _) =>
            Result<CabinetBuildResult>.Failure(ErrorKind.CompilationError, "specific error text"));

        var workItems = new[] { MakeWorkItem("FailSingle", 2) };
        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 4, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Equal("specific error text", result.Error.Message);
    }

    [Fact]
    public async Task BuildAsync_SingleItem_Cancellation_ReturnsFailureWithCancelledMessage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var builder = new ParallelCabinetBuilder((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new CabinetBuildResult("X", "path", 1, 1);
        });

        var workItems = new[] { MakeWorkItem("Cancelled", 1) };
        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Equal("Cabinet build was cancelled.", result.Error.Message);
    }

    [Fact]
    public async Task BuildAsync_TwoItems_UsesParallelPath()
    {
        var builder = new ParallelCabinetBuilder((workItem, _) =>
            new CabinetBuildResult(
                workItem.CabinetName,
                $@"C:\out\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                512L));

        var workItems = new[]
        {
            MakeWorkItem("Alpha", 2),
            MakeWorkItem("Beta", 5),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task BuildAsync_MultipleItems_PreservesOrder()
    {
        var builder = new ParallelCabinetBuilder((workItem, _) =>
        {
            // Add some jitter to potentially reorder
            if (workItem.CabinetName == "A")
                Thread.Sleep(20);
            return new CabinetBuildResult(
                workItem.CabinetName,
                $@"C:\out\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                100L);
        });

        var workItems = new[]
        {
            MakeWorkItem("A", 1),
            MakeWorkItem("B", 1),
            MakeWorkItem("C", 1),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 4, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Order must be preserved by index mapping
        Assert.Equal("A", result.Value[0].CabinetName);
        Assert.Equal("B", result.Value[1].CabinetName);
        Assert.Equal("C", result.Value[2].CabinetName);
    }

    [Fact]
    public async Task BuildAsync_MultipleItems_OneFailure_ReturnsFailure()
    {
        var builder = new ParallelCabinetBuilder((workItem, _) =>
        {
            if (workItem.CabinetName == "Middle")
                return Result<CabinetBuildResult>.Failure(
                    ErrorKind.CompilationError, "Middle failed");
            return new CabinetBuildResult(
                workItem.CabinetName, "path",
                workItem.FileEntries.Count, 100L);
        });

        var workItems = new[]
        {
            MakeWorkItem("First", 1),
            MakeWorkItem("Middle", 1),
            MakeWorkItem("Last", 1),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Middle", result.Error.Message);
    }

    [Fact]
    public async Task BuildAsync_MultipleItems_FirstError_ShortCircuitsSubsequent()
    {
        var callCount = 0;
        var builder = new ParallelCabinetBuilder((workItem, _) =>
        {
            var count = Interlocked.Increment(ref callCount);
            if (count == 1)
                return Result<CabinetBuildResult>.Failure(
                    ErrorKind.CompilationError, "first error");
            // Subsequent items should be short-circuited
            return new CabinetBuildResult(
                workItem.CabinetName, "path",
                workItem.FileEntries.Count, 100L);
        });

        var workItems = Enumerable.Range(1, 10)
            .Select(i => MakeWorkItem($"Cab{i}", 1))
            .ToList();

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("first error", result.Error.Message);
        // With parallelism=1 and short-circuit, not all items should be processed
        Assert.True(callCount < 10, $"Expected short-circuit but {callCount} items were processed");
    }

    [Fact]
    public async Task BuildAsync_MultipleItems_Cancellation_ReturnsFailureWithCancelledMessage()
    {
        using var cts = new CancellationTokenSource();

        var builder = new ParallelCabinetBuilder((workItem, ct) =>
        {
            if (workItem.CabinetName == "Trigger")
                cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return new CabinetBuildResult(
                workItem.CabinetName, "path",
                workItem.FileEntries.Count, 100L);
        });

        var workItems = new[]
        {
            MakeWorkItem("Trigger", 1),
            MakeWorkItem("After", 1),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Equal("Cabinet build was cancelled.", result.Error.Message);
    }

    [Fact]
    public async Task BuildAsync_MultipleItems_AllSucceed_EachResultHasCorrectValues()
    {
        var builder = new ParallelCabinetBuilder((workItem, _) =>
            new CabinetBuildResult(
                workItem.CabinetName,
                $@"D:\output\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                workItem.FileEntries.Count * 256L));

        var workItems = new[]
        {
            MakeWorkItem("X", 3),
            MakeWorkItem("Y", 7),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 2, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var x = result.Value.First(r => r.CabinetName == "X");
        Assert.Equal(@"D:\output\X.cab", x.OutputPath);
        Assert.Equal(3, x.FileCount);
        Assert.Equal(768L, x.CompressedSize);

        var y = result.Value.First(r => r.CabinetName == "Y");
        Assert.Equal(@"D:\output\Y.cab", y.OutputPath);
        Assert.Equal(7, y.FileCount);
        Assert.Equal(1792L, y.CompressedSize);
    }

    [Fact]
    public async Task BuildAsync_SingleItem_IsFailure_PropagatesErrorKind()
    {
        var builder = new ParallelCabinetBuilder((_, _) =>
            Result<CabinetBuildResult>.Failure(ErrorKind.IoError, "disk full"));

        var workItems = new[] { MakeWorkItem("DiskFull", 1) };
        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IoError, result.Error.Kind);
        Assert.Equal("disk full", result.Error.Message);
    }

    [Fact]
    public void CabinetWorkItem_Properties_AreAccessible()
    {
        var files = new[] { MakeFile("test.dll") };
        var item = new CabinetWorkItem("TestCab", files, CompressionLevel.Medium);

        Assert.Equal("TestCab", item.CabinetName);
        Assert.Same(files, item.FileEntries);
        Assert.Equal(CompressionLevel.Medium, item.CompressionLevel);
    }

    [Fact]
    public void CabinetBuildResult_Properties_AreAccessible()
    {
        var result = new CabinetBuildResult("MyCab", @"C:\out\MyCab.cab", 42, 99999L);

        Assert.Equal("MyCab", result.CabinetName);
        Assert.Equal(@"C:\out\MyCab.cab", result.OutputPath);
        Assert.Equal(42, result.FileCount);
        Assert.Equal(99999L, result.CompressedSize);
    }

    [Fact]
    public void CabinetBuildResult_DifferentFileCount_AreNotEqual()
    {
        var a = new CabinetBuildResult("Cab", "path", 5, 100);
        var b = new CabinetBuildResult("Cab", "path", 6, 100);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CabinetBuildResult_DifferentOutputPath_AreNotEqual()
    {
        var a = new CabinetBuildResult("Cab", @"C:\a.cab", 5, 100);
        var b = new CabinetBuildResult("Cab", @"C:\b.cab", 5, 100);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CabinetBuildResult_DifferentCabinetName_AreNotEqual()
    {
        var a = new CabinetBuildResult("Cab1", "path", 5, 100);
        var b = new CabinetBuildResult("Cab2", "path", 5, 100);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CabinetWorkItem_DifferentCompressionLevel_AreNotEqual()
    {
        var files = new[] { MakeFile("a.dll") };
        var a = new CabinetWorkItem("Cab", files, CompressionLevel.High);
        var b = new CabinetWorkItem("Cab", files, CompressionLevel.Low);

        Assert.NotEqual(a, b);
    }
}
