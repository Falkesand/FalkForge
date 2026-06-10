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

        // All tasks completed — InstallerState must be thread-safe for concurrent set/get/remove.
        Assert.True(tasks.All(t => t.IsCompletedSuccessfully), "All concurrent tasks completed without exception.");
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

    [Fact]
    public async Task Concurrent_SetSensitive_and_Dispose_does_not_corrupt_memory()
    {
        // 4 threads call SetSensitive, 4 threads call Dispose simultaneously.
        // Acceptable outcomes: no exception, or ObjectDisposedException.
        // Unacceptable: any other exception type (e.g., NullReferenceException).
        var protector = new FakeProtector();
        var state = new InstallerState(protector);
        var barrier = new Barrier(8);

        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            try
            {
                if (i < 4)
                    state.SetSensitive($"Key{i}", new byte[] { (byte)i, (byte)(i + 1) });
                else
                    state.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Expected: SetSensitive after Dispose.
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // All tasks completed — only ObjectDisposedException is acceptable; no NullReferenceException or other crash.
        Assert.True(tasks.All(t => t.IsCompletedSuccessfully), "No unexpected exception type escaped concurrent SetSensitive/Dispose.");
    }

    [Fact]
    public async Task Concurrent_SetSensitive_and_GetSensitive_without_Dispose_is_coherent()
    {
        // Last writer wins; reads always return a valid (possibly stale) snapshot.
        var protector = new FakeProtector();
        var state = new InstallerState(protector);
        const string key = "SharedKey";
        state.SetSensitive(key, new byte[] { 0x00 });
        var barrier = new Barrier(8);

        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var iter = 0; iter < 200; iter++)
            {
                if (i % 2 == 0)
                {
                    state.SetSensitive(key, new byte[] { (byte)i });
                }
                else
                {
                    using var result = state.GetSensitive(key);
                    // Must be a single byte — any value is valid (last-writer-wins).
                    Assert.True(result.IsEmpty || result.Length == 1,
                        $"Expected empty or 1-byte result, got length {result.Length}");
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        state.Dispose();
    }

    [Fact]
    public void SetSensitive_after_Dispose_throws_ObjectDisposedException()
    {
        var protector = new FakeProtector();
        var state = new InstallerState(protector);
        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => state.SetSensitive("Key", new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void GetSensitive_after_Dispose_throws_ObjectDisposedException()
    {
        var protector = new FakeProtector();
        var state = new InstallerState(protector);
        state.SetSensitive("Key", new byte[] { 1 });
        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => state.GetSensitive("Key"));
    }

    [Fact]
    public void Dispose_zeroes_underlying_bytes_of_stored_sensitive_value()
    {
        // The TrackingProtector keeps a reference to the protected byte[] stored inside
        // InstallerState. After Dispose, InstallerState must ZeroMemory that array.
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);
        state.SetSensitive("Secret", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var storedBytes = protector.LastProtected!;

        // Verify bytes are non-zero before dispose.
        Assert.Contains(storedBytes, b => b != 0);

        state.Dispose();

        // All stored bytes must be zeroed.
        Assert.All(storedBytes, b => Assert.Equal(0, b));
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
