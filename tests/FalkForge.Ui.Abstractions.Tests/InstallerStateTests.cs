namespace FalkForge.Ui.Abstractions.Tests;

using Xunit;

public sealed class InstallerStateTests
{
    [Fact]
    public void Set_and_get_string_value()
    {
        var state = new InstallerState();

        state.Set("Name", "TestApp");

        Assert.Equal("TestApp", state.Get<string>("Name"));
    }

    [Fact]
    public void Set_and_get_int_value()
    {
        var state = new InstallerState();

        state.Set("Port", 8080);

        Assert.Equal(8080, state.Get<int>("Port"));
    }

    [Fact]
    public void Get_missing_key_returns_null_for_reference_type()
    {
        var state = new InstallerState();

        Assert.Null(state.Get<string>("Missing"));
    }

    [Fact]
    public void Get_missing_key_returns_default_for_value_type()
    {
        var state = new InstallerState();

        Assert.Equal(0, state.Get<int>("Missing"));
    }

    [Fact]
    public void Get_with_wrong_type_returns_default()
    {
        var state = new InstallerState();
        state.Set("Port", 8080);

        Assert.Null(state.Get<string>("Port"));
    }

    [Fact]
    public void ContainsKey_returns_true_for_existing_key()
    {
        var state = new InstallerState();
        state.Set("Key", "Value");

        Assert.True(state.ContainsKey("Key"));
    }

    [Fact]
    public void ContainsKey_returns_false_for_missing_key()
    {
        var state = new InstallerState();

        Assert.False(state.ContainsKey("Missing"));
    }

    [Fact]
    public void Remove_returns_true_for_existing_key()
    {
        var state = new InstallerState();
        state.Set("Key", "Value");

        Assert.True(state.Remove("Key"));
    }

    [Fact]
    public void Remove_returns_false_for_missing_key()
    {
        var state = new InstallerState();

        Assert.False(state.Remove("Missing"));
    }

    [Fact]
    public void Remove_makes_key_no_longer_available()
    {
        var state = new InstallerState();
        state.Set("Key", "Value");

        state.Remove("Key");

        Assert.False(state.ContainsKey("Key"));
        Assert.Null(state.Get<string>("Key"));
    }

    [Fact]
    public void InstallDirectory_get_returns_null_when_not_set()
    {
        var state = new InstallerState();

        Assert.Null(state.InstallDirectory);
    }

    [Fact]
    public void InstallDirectory_set_and_get()
    {
        var state = new InstallerState();

        state.InstallDirectory = @"C:\Program Files\TestApp";

        Assert.Equal(@"C:\Program Files\TestApp", state.InstallDirectory);
    }

    [Fact]
    public void InstallDirectory_set_to_null_removes_key()
    {
        var state = new InstallerState();
        state.InstallDirectory = @"C:\Program Files\TestApp";

        state.InstallDirectory = null;

        Assert.Null(state.InstallDirectory);
        Assert.False(state.ContainsKey("InstallDirectory"));
    }

    [Fact]
    public void InstallDirectory_is_accessible_via_generic_get()
    {
        var state = new InstallerState();
        state.InstallDirectory = @"C:\Apps";

        Assert.Equal(@"C:\Apps", state.Get<string>("InstallDirectory"));
    }

    [Fact]
    public async Task Concurrent_set_and_get_does_not_throw()
    {
        var state = new InstallerState();
        const int threadCount = 10;
        const int iterationsPerThread = 1000;
        var barrier = new Barrier(threadCount);

        var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                var key = $"Key_{threadIndex}_{i}";
                state.Set(key, i);
                _ = state.Get<int>(key);
                state.ContainsKey(key);
                state.Remove(key);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
    }
}
