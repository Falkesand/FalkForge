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

    [Fact]
    public void SetSensitive_and_GetSensitive_roundtrips()
    {
        var protector = new FakeProtector();
        var state = new InstallerState(protector);
        var data = new byte[] { 0x41, 0x42, 0x43 };

        state.SetSensitive("Password", data);
        using var result = state.GetSensitive("Password");

        Assert.Equal(data, result.Span.ToArray());
    }

    [Fact]
    public void GetSensitive_missing_key_returns_empty()
    {
        var state = new InstallerState(new FakeProtector());

        using var result = state.GetSensitive("Missing");

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void SetSensitive_overwrites_and_zeroes_old_value()
    {
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);

        state.SetSensitive("Key", [1, 2, 3]);
        var firstProtected = protector.LastProtected!;

        state.SetSensitive("Key", [4, 5, 6]);

        Assert.All(firstProtected, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Dispose_zeroes_all_sensitive_entries()
    {
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);
        state.SetSensitive("A", [1, 2, 3]);
        state.SetSensitive("B", [4, 5, 6]);
        var protectedA = protector.AllProtected[0];
        var protectedB = protector.AllProtected[1];

        state.Dispose();

        Assert.All(protectedA, b => Assert.Equal(0, b));
        Assert.All(protectedB, b => Assert.Equal(0, b));
    }

    [Fact]
    public void RemoveSensitive_zeroes_and_removes()
    {
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);
        state.SetSensitive("Key", [1, 2, 3]);
        var protectedValue = protector.LastProtected!;

        var removed = state.RemoveSensitive("Key");

        Assert.True(removed);
        Assert.All(protectedValue, b => Assert.Equal(0, b));
        using var afterRemove = state.GetSensitive("Key");
        Assert.True(afterRemove.IsEmpty);
    }

    [Fact]
    public void RemoveSensitive_returns_false_for_missing_key()
    {
        var state = new InstallerState(new FakeProtector());

        Assert.False(state.RemoveSensitive("Missing"));
    }

    [Fact]
    public void Parameterless_constructor_still_works()
    {
        var state = new InstallerState();

        state.Set("Key", "Value");

        Assert.Equal("Value", state.Get<string>("Key"));
    }

    [Fact]
    public void SetSensitive_without_protector_throws()
    {
        var state = new InstallerState();

        Assert.Throws<InvalidOperationException>(() => state.SetSensitive("Key", [1, 2]));
    }

    private sealed class FakeProtector : ISensitiveDataProtector
    {
        public byte[] Protect(byte[] plainData) => [.. plainData];
        public byte[] Unprotect(byte[] protectedData) => [.. protectedData];
    }

    private sealed class TrackingProtector : ISensitiveDataProtector
    {
        public byte[]? LastProtected { get; private set; }
        public List<byte[]> AllProtected { get; } = [];

        public byte[] Protect(byte[] plainData)
        {
            var result = new byte[plainData.Length];
            Array.Copy(plainData, result, plainData.Length);
            LastProtected = result;
            AllProtected.Add(result);
            return result;
        }

        public byte[] Unprotect(byte[] protectedData) => [.. protectedData];
    }
}
