namespace FalkForge.Ui.Abstractions.Tests;

using Xunit;

public sealed class SensitiveBytesTests
{
    [Fact]
    public void Span_returns_underlying_data()
    {
        var data = new byte[] { 1, 2, 3 };
        var sensitive = new SensitiveBytes(data);

        Assert.Equal(data, sensitive.Span.ToArray());
    }

    [Fact]
    public void Length_returns_byte_count()
    {
        var sensitive = new SensitiveBytes(new byte[] { 1, 2, 3 });

        Assert.Equal(3, sensitive.Length);
    }

    [Fact]
    public void IsEmpty_returns_true_for_null()
    {
        var sensitive = new SensitiveBytes(null!);

        Assert.True(sensitive.IsEmpty);
    }

    [Fact]
    public void IsEmpty_returns_true_for_empty_array()
    {
        var sensitive = new SensitiveBytes([]);

        Assert.True(sensitive.IsEmpty);
    }

    [Fact]
    public void IsEmpty_returns_false_for_data()
    {
        var sensitive = new SensitiveBytes(new byte[] { 1 });

        Assert.False(sensitive.IsEmpty);
    }

    [Fact]
    public void Default_IsEmptyWithZeroLength()
    {
        SensitiveBytes sensitive = default;

        Assert.True(sensitive.IsEmpty);
        Assert.Equal(0, sensitive.Length);
    }

    [Fact]
    public void Dispose_zeroes_underlying_array()
    {
        var data = new byte[] { 0x41, 0x42, 0x43 };
        var sensitive = new SensitiveBytes(data);

        sensitive.Dispose();

        Assert.All(data, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Dispose_with_null_does_not_throw()
    {
        var sensitive = new SensitiveBytes(null!);

        sensitive.Dispose();
    }

    [Fact]
    public void Using_pattern_zeroes_on_scope_exit()
    {
        var data = new byte[] { 0x41, 0x42, 0x43 };

        using (var _ = new SensitiveBytes(data)) { }

        Assert.All(data, b => Assert.Equal(0, b));
    }
}
