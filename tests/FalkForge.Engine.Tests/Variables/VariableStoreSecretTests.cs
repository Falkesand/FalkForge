namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using Xunit;

public sealed class VariableStoreSecretTests
{
    [Fact]
    public void SetSecret_GetSecret_ReturnsValue()
    {
        using var store = new VariableStore();
        store.SetSecret("Password", "s3cret!");

        var result = store.GetSecret("Password");

        Assert.True(result.IsSuccess);
        Assert.Equal("s3cret!", result.Value);
    }

    [Fact]
    public void SetSecret_IsSecret_ReturnsTrue()
    {
        using var store = new VariableStore();
        store.SetSecret("ApiKey", "abc123");

        Assert.True(store.IsSecret("ApiKey"));
    }

    [Fact]
    public void IsSecret_RegularVariable_ReturnsFalse()
    {
        using var store = new VariableStore();
        store.Set("InstallDir", @"C:\App");

        Assert.False(store.IsSecret("InstallDir"));
    }

    [Fact]
    public void SetSecret_OverwritesPrevious_DisposesPrevious()
    {
        using var store = new VariableStore();

        // Set initial secret
        store.SetSecret("Token", "old-value");

        // Capture the internal SecureVariable to verify disposal
        var secretsField = typeof(VariableStore).GetField("_secrets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var secrets = (System.Collections.Concurrent.ConcurrentDictionary<string, SecureVariable>)secretsField.GetValue(store)!;
        var oldSecure = secrets["Token"];

        // Overwrite
        store.SetSecret("Token", "new-value");

        // Old SecureVariable should be disposed (GetValue throws)
        Assert.Throws<ObjectDisposedException>(() => oldSecure.GetValue());

        // New value should be retrievable
        var result = store.GetSecret("Token");
        Assert.True(result.IsSuccess);
        Assert.Equal("new-value", result.Value);
    }

    [Fact]
    public void DisposeSecrets_ClearsAllSecrets()
    {
        using var store = new VariableStore();
        store.SetSecret("Secret1", "val1");
        store.SetSecret("Secret2", "val2");

        store.DisposeSecrets();

        Assert.False(store.IsSecret("Secret1"));
        Assert.False(store.IsSecret("Secret2"));
        Assert.True(store.GetSecret("Secret1").IsFailure);
        Assert.True(store.GetSecret("Secret2").IsFailure);
    }

    [Fact]
    public void GetRaw_SecretVariable_ReturnsStringValue()
    {
        using var store = new VariableStore();
        store.SetSecret("ConnectionString", "Server=db;Password=p@ss");

        // GetRaw is internal, used by ConditionEvaluator
        var raw = store.GetRaw("ConnectionString");

        Assert.NotNull(raw);
        Assert.Equal("Server=db;Password=p@ss", raw);
    }

    [Fact]
    public void SetSecretBytes_GetSecret_ReturnsValue()
    {
        using var store = new VariableStore();
        var bytes = System.Text.Encoding.UTF8.GetBytes("s3cret!");
        store.SetSecret("Password", bytes);

        var result = store.GetSecret("Password");

        Assert.True(result.IsSuccess);
        Assert.Equal("s3cret!", result.Value);
    }

    [Fact]
    public void SetSecretBytes_OverwritesPrevious_DisposesPrevious()
    {
        using var store = new VariableStore();

        // Set initial secret via byte[]
        var oldBytes = System.Text.Encoding.UTF8.GetBytes("old-value");
        store.SetSecret("Token", oldBytes);

        // Capture the internal SecureVariable to verify disposal
        var secretsField = typeof(VariableStore).GetField("_secrets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var secrets = (System.Collections.Concurrent.ConcurrentDictionary<string, SecureVariable>)secretsField.GetValue(store)!;
        var oldSecure = secrets["Token"];

        // Overwrite with new byte[]
        var newBytes = System.Text.Encoding.UTF8.GetBytes("new-value");
        store.SetSecret("Token", newBytes);

        // Old SecureVariable should be disposed (GetValue throws)
        Assert.Throws<ObjectDisposedException>(() => oldSecure.GetValue());

        // Old bytes should be zeroed
        Assert.All(oldBytes, b => Assert.Equal(0, b));

        // New value should be retrievable
        var result = store.GetSecret("Token");
        Assert.True(result.IsSuccess);
        Assert.Equal("new-value", result.Value);
    }

    [Fact]
    public void SetSecretBytes_GetRaw_ReturnsStringValue()
    {
        using var store = new VariableStore();
        var bytes = System.Text.Encoding.UTF8.GetBytes("Server=db;Password=p@ss");
        store.SetSecret("ConnectionString", bytes);

        var raw = store.GetRaw("ConnectionString");

        Assert.NotNull(raw);
        Assert.Equal("Server=db;Password=p@ss", raw);
    }
}
