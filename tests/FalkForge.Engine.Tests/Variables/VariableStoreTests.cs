namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using Xunit;

public sealed class VariableStoreTests
{
    [Fact]
    public void Set_StringValue_CanBeRetrieved()
    {
        var store = new VariableStore();
        store.Set("MyVar", "hello");

        var result = store.TryGet<string>("MyVar");

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Set_LongValue_CanBeRetrieved()
    {
        var store = new VariableStore();
        store.Set("Count", 42L);

        var result = store.TryGet<long>("Count");

        Assert.True(result.IsSuccess);
        Assert.Equal(42L, result.Value);
    }

    [Fact]
    public void Set_VersionValue_CanBeRetrieved()
    {
        var store = new VariableStore();
        var version = new Version(1, 2, 3);
        store.Set("Ver", version);

        var result = store.TryGet<Version>("Ver");

        Assert.True(result.IsSuccess);
        Assert.Equal(new Version(1, 2, 3), result.Value);
    }

    [Fact]
    public void TryGet_MissingVariable_ReturnsFailure()
    {
        var store = new VariableStore();

        var result = store.TryGet<string>("NonExistent");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void TryGet_WrongType_ReturnsFailure()
    {
        var store = new VariableStore();
        store.Set("MyVar", "text");

        var result = store.TryGet<long>("MyVar");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Contains_ExistingVariable_ReturnsTrue()
    {
        var store = new VariableStore();
        store.Set("Present", "value");

        Assert.True(store.Contains("Present"));
    }

    [Fact]
    public void Contains_MissingVariable_ReturnsFalse()
    {
        var store = new VariableStore();

        Assert.False(store.Contains("Missing"));
    }

    [Fact]
    public void Set_CaseInsensitiveLookup_FindsVariable()
    {
        var store = new VariableStore();
        store.Set("MyVariable", "found");

        var result = store.TryGet<string>("myvariable");

        Assert.True(result.IsSuccess);
        Assert.Equal("found", result.Value);
    }

    [Fact]
    public void Contains_CaseInsensitive_ReturnsTrue()
    {
        var store = new VariableStore();
        store.Set("VersionNT", "10.0");

        Assert.True(store.Contains("versionnt"));
        Assert.True(store.Contains("VERSIONNT"));
    }

    [Fact]
    public void GetString_FromLong_ReturnsStringRepresentation()
    {
        var store = new VariableStore();
        store.Set("Num", 123L);

        var result = store.GetString("Num");

        Assert.True(result.IsSuccess);
        Assert.Equal("123", result.Value);
    }

    [Fact]
    public void GetString_FromVersion_ReturnsStringRepresentation()
    {
        var store = new VariableStore();
        store.Set("Ver", new Version(6, 1, 0));

        var result = store.GetString("Ver");

        Assert.True(result.IsSuccess);
        Assert.Equal("6.1.0", result.Value);
    }

    [Fact]
    public void GetString_Missing_ReturnsFailure()
    {
        var store = new VariableStore();

        var result = store.GetString("Nope");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void GetInt_FromString_ParsesSuccessfully()
    {
        var store = new VariableStore();
        store.Set("Num", "999");

        var result = store.GetInt("Num");

        Assert.True(result.IsSuccess);
        Assert.Equal(999L, result.Value);
    }

    [Fact]
    public void GetInt_FromNonNumericString_ReturnsFailure()
    {
        var store = new VariableStore();
        store.Set("Num", "not-a-number");

        var result = store.GetInt("Num");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void GetVersion_FromString_ParsesSuccessfully()
    {
        var store = new VariableStore();
        store.Set("Ver", "1.2.3.4");

        var result = store.GetVersion("Ver");

        Assert.True(result.IsSuccess);
        Assert.Equal(new Version(1, 2, 3, 4), result.Value);
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        var store = new VariableStore();
        store.Set("Key", "first");
        store.Set("Key", "second");

        var result = store.TryGet<string>("Key");

        Assert.True(result.IsSuccess);
        Assert.Equal("second", result.Value);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentSetAndGet_DoesNotThrow()
    {
        var store = new VariableStore();
        var tasks = new Task[100];

        for (var i = 0; i < tasks.Length; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                store.Set($"Var{index}", $"Value{index}");
                store.Contains($"Var{index}");
                store.TryGet<string>($"Var{index}");
            });
        }

        await Task.WhenAll(tasks);

        // All 100 variables should exist
        for (var i = 0; i < 100; i++)
        {
            Assert.True(store.Contains($"Var{i}"));
        }
    }
}
