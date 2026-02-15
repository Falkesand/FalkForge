using System.Collections.Concurrent;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class ParallelCabinetBuilderTests
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

    private static Func<CabinetWorkItem, CancellationToken, Result<CabinetBuildResult>> FakeBuilder(
        TimeSpan? delay = null)
    {
        return (workItem, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (delay.HasValue)
                Thread.Sleep(delay.Value);
            ct.ThrowIfCancellationRequested();
            return new CabinetBuildResult(
                workItem.CabinetName,
                $@"C:\output\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                workItem.FileEntries.Count * 512L);
        };
    }

    private static Func<CabinetWorkItem, CancellationToken, Result<CabinetBuildResult>> FailingBuilder(
        string failCabinetName)
    {
        return (workItem, ct) =>
        {
            if (workItem.CabinetName == failCabinetName)
                return Result<CabinetBuildResult>.Failure(ErrorKind.CompilationError, $"Build failed for {failCabinetName}");
            return new CabinetBuildResult(
                workItem.CabinetName,
                $@"C:\output\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                workItem.FileEntries.Count * 512L);
        };
    }

    [Fact]
    public async Task BuildAsync_EmptyWorkItems_ReturnsEmptyList()
    {
        var builder = new ParallelCabinetBuilder(FakeBuilder());

        var result = await builder.BuildAsync([], maxDegreeOfParallelism: 4, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task BuildAsync_SingleWorkItem_ReturnsOneResult()
    {
        var builder = new ParallelCabinetBuilder(FakeBuilder());
        var workItems = new[] { MakeWorkItem("Data1", 5) };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 4, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Data1", result.Value[0].CabinetName);
        Assert.Equal(5, result.Value[0].FileCount);
    }

    [Fact]
    public async Task BuildAsync_MultipleWorkItems_ReturnsAllResults()
    {
        var builder = new ParallelCabinetBuilder(FakeBuilder());
        var workItems = new[]
        {
            MakeWorkItem("Cab1", 3),
            MakeWorkItem("Cab2", 7),
            MakeWorkItem("Cab3", 2),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 4, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        var names = result.Value.Select(r => r.CabinetName).OrderBy(n => n).ToList();
        Assert.Equal(["Cab1", "Cab2", "Cab3"], names);
    }

    [Fact]
    public async Task BuildAsync_MultipleWorkItems_PreservesFileCount()
    {
        var builder = new ParallelCabinetBuilder(FakeBuilder());
        var workItems = new[]
        {
            MakeWorkItem("A", 10),
            MakeWorkItem("B", 20),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var lookup = result.Value.ToDictionary(r => r.CabinetName);
        Assert.Equal(10, lookup["A"].FileCount);
        Assert.Equal(20, lookup["B"].FileCount);
    }

    [Fact]
    public async Task BuildAsync_Cancellation_ThrowsOrReturnsFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var builder = new ParallelCabinetBuilder(FakeBuilder(delay: TimeSpan.FromSeconds(5)));
        var workItems = new[] { MakeWorkItem("Slow", 10) };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Fact]
    public async Task BuildAsync_CancellationDuringExecution_StopsProcessing()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        var builder = new ParallelCabinetBuilder((workItem, ct) =>
        {
            var count = Interlocked.Increment(ref callCount);
            if (count >= 2)
                cts.Cancel();
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(50);
            ct.ThrowIfCancellationRequested();
            return new CabinetBuildResult(workItem.CabinetName, "path", workItem.FileEntries.Count, 100);
        });

        var workItems = Enumerable.Range(1, 20).Select(i => MakeWorkItem($"Cab{i}", 1)).ToList();

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, cts.Token);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task BuildAsync_OneWorkItemFails_ReturnsFailure()
    {
        var builder = new ParallelCabinetBuilder(FailingBuilder("Bad"));
        var workItems = new[]
        {
            MakeWorkItem("Good1", 5),
            MakeWorkItem("Bad", 3),
            MakeWorkItem("Good2", 4),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Bad", result.Error.Message);
    }

    [Fact]
    public async Task BuildAsync_MaxDegreeOfParallelism_LimitsConcurrency()
    {
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var lockObj = new object();

        var builder = new ParallelCabinetBuilder((workItem, ct) =>
        {
            var current = Interlocked.Increment(ref currentConcurrent);
            lock (lockObj)
            {
                if (current > maxConcurrent)
                    maxConcurrent = current;
            }
            Thread.Sleep(50);
            Interlocked.Decrement(ref currentConcurrent);
            return new CabinetBuildResult(workItem.CabinetName, "path", workItem.FileEntries.Count, 100);
        });

        var workItems = Enumerable.Range(1, 8).Select(i => MakeWorkItem($"Cab{i}", 1)).ToList();

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(maxConcurrent <= 2, $"Expected max concurrency of 2 but observed {maxConcurrent}");
    }

    [Fact]
    public async Task BuildAsync_MaxDegreeOfParallelismOne_RunsSequentially()
    {
        var maxConcurrent = 0;
        var currentConcurrent = 0;

        var builder = new ParallelCabinetBuilder((workItem, ct) =>
        {
            var current = Interlocked.Increment(ref currentConcurrent);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            Thread.Sleep(10);
            Interlocked.Decrement(ref currentConcurrent);
            return new CabinetBuildResult(workItem.CabinetName, "path", workItem.FileEntries.Count, 100);
        });

        var workItems = Enumerable.Range(1, 4).Select(i => MakeWorkItem($"Cab{i}", 1)).ToList();

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, maxConcurrent);
    }

    [Fact]
    public async Task BuildAsync_CompressedSize_AccumulatedCorrectly()
    {
        var builder = new ParallelCabinetBuilder((workItem, ct) =>
            new CabinetBuildResult(workItem.CabinetName, "path", workItem.FileEntries.Count, 999));

        var workItems = new[]
        {
            MakeWorkItem("X", 1),
            MakeWorkItem("Y", 2),
        };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value, r => Assert.Equal(999, r.CompressedSize));
    }

    [Fact]
    public async Task BuildAsync_OutputPaths_PreservedFromDelegate()
    {
        var builder = new ParallelCabinetBuilder((workItem, ct) =>
            new CabinetBuildResult(
                workItem.CabinetName,
                $@"C:\out\{workItem.CabinetName}.cab",
                workItem.FileEntries.Count,
                100));

        var workItems = new[] { MakeWorkItem("MyData", 3) };

        var result = await builder.BuildAsync(workItems, maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\out\MyData.cab", result.Value[0].OutputPath);
    }

    [Fact]
    public void CabinetWorkItem_Equality_SameValues_AreEqual()
    {
        var files = new[] { MakeFile("a.dll") };
        var a = new CabinetWorkItem("Cab1", files, CompressionLevel.High);
        var b = new CabinetWorkItem("Cab1", files, CompressionLevel.High);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CabinetWorkItem_Equality_DifferentName_AreNotEqual()
    {
        var files = new[] { MakeFile("a.dll") };
        var a = new CabinetWorkItem("Cab1", files, CompressionLevel.High);
        var b = new CabinetWorkItem("Cab2", files, CompressionLevel.High);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CabinetBuildResult_Equality_SameValues_AreEqual()
    {
        var a = new CabinetBuildResult("Cab1", @"C:\out\Cab1.cab", 5, 2048);
        var b = new CabinetBuildResult("Cab1", @"C:\out\Cab1.cab", 5, 2048);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CabinetBuildResult_Equality_DifferentSize_AreNotEqual()
    {
        var a = new CabinetBuildResult("Cab1", @"C:\out\Cab1.cab", 5, 2048);
        var b = new CabinetBuildResult("Cab1", @"C:\out\Cab1.cab", 5, 4096);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PackageBuilder_CabinetThreads_SetsCorrectValue()
    {
        var builder = new Builders.PackageBuilder
        {
            Name = "TestApp",
            Manufacturer = "TestCorp",
        };

        builder.CabinetThreads(8);

        var model = builder.Build();
        Assert.Equal(8, model.CabinetThreadCount);
    }

    [Fact]
    public void PackageBuilder_CabinetThreads_DefaultIsZero()
    {
        var builder = new Builders.PackageBuilder
        {
            Name = "TestApp",
            Manufacturer = "TestCorp",
        };

        var model = builder.Build();
        Assert.Equal(0, model.CabinetThreadCount);
    }

    [Fact]
    public void PackageBuilder_CabinetThreads_ReturnsSelf()
    {
        var builder = new Builders.PackageBuilder
        {
            Name = "TestApp",
            Manufacturer = "TestCorp",
        };

        var result = builder.CabinetThreads(4);

        Assert.Same(builder, result);
    }
}
