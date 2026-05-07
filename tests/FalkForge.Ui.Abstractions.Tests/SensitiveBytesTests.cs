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

    // --- FromPlaintext factory ---

    [Fact]
    public void FromPlaintext_copies_span_into_new_backing_array()
    {
        var original = new byte[] { 0x11, 0x22, 0x33 };
        ReadOnlySpan<byte> span = original;

        using var sensitive = SensitiveBytes.FromPlaintext(span);

        Assert.Equal(original, sensitive.Span.ToArray());
    }

    [Fact]
    public void FromPlaintext_isolates_copy_from_original_array()
    {
        // Mutating original after factory call must not affect SensitiveBytes content.
        var original = new byte[] { 0xAA, 0xBB };
        using var sensitive = SensitiveBytes.FromPlaintext(original);

        original[0] = 0x00;

        Assert.Equal(0xAA, sensitive.Span[0]);
    }

    [Fact]
    public void FromPlaintext_empty_span_produces_empty_instance()
    {
        using var sensitive = SensitiveBytes.FromPlaintext(ReadOnlySpan<byte>.Empty);

        Assert.True(sensitive.IsEmpty);
        Assert.Equal(0, sensitive.Length);
    }

    // --- Borrow reveal scope ---

    [Fact]
    public void Borrow_exposes_plaintext_as_span_inside_scope()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        using var sensitive = SensitiveBytes.FromPlaintext(data);

        using var reveal = sensitive.Borrow();

        Assert.Equal(data, reveal.Span.ToArray());
        Assert.Equal(3, reveal.Length);
    }

    [Fact]
    public void Borrow_on_empty_sensitive_bytes_succeeds_with_empty_span()
    {
        using var sensitive = SensitiveBytes.FromPlaintext(ReadOnlySpan<byte>.Empty);

        using var reveal = sensitive.Borrow();

        Assert.True(reveal.Span.IsEmpty);
        Assert.Equal(0, reveal.Length);
    }

    [Fact]
    public void Borrow_does_not_zero_backing_array_on_dispose()
    {
        var data = new byte[] { 0xDE, 0xAD };
        using var sensitive = SensitiveBytes.FromPlaintext(data);

        using (sensitive.Borrow()) { }

        // The underlying SensitiveBytes is still readable after the borrow scope closes.
        Assert.Equal(0xDE, sensitive.Span[0]);
        Assert.Equal(0xAD, sensitive.Span[1]);
    }
}
