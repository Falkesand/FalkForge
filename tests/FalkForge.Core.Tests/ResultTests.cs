using Xunit;

namespace FalkForge.Core.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        var error = new Error(ErrorKind.Validation, "bad input");

        var result = Result<int>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal("bad input", result.Error.Message);
    }

    [Fact]
    public void Failure_WithKindAndMessage_CreatesFailedResult()
    {
        var result = Result<string>.Failure(ErrorKind.FileNotFound, "not found");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Equal("not found", result.Error.Message);
    }

    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccessResult()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Success_WithNullValue_CreatesSuccessfulResultCarryingNull()
    {
        var result = Result<string?>.Success(null);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromNullValue_CreatesSuccessResultCarryingNull()
    {
        string? value = null;

        Result<string?> result = value;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Map_WhenMappingFunctionReturnsNull_CreatesSuccessResultCarryingNull()
    {
        var result = Result<int>.Success(1);

        var mapped = result.Map(_ => (string?)null);

        Assert.True(mapped.IsSuccess);
        Assert.Null(mapped.Value);
    }

    [Fact]
    public void Value_OnFailure_ThrowsInvalidOperationException()
    {
        var result = Result<int>.Failure(ErrorKind.Validation, "err");

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Cannot access Value on failed result", ex.Message);
    }

    [Fact]
    public void Error_OnSuccess_ThrowsInvalidOperationException()
    {
        var result = Result<int>.Success(1);

        var ex = Assert.Throws<InvalidOperationException>(() => result.Error);
        Assert.Contains("Cannot access Error on successful result", ex.Message);
    }

    [Fact]
    public void Match_OnSuccess_InvokesOnSuccessFunc()
    {
        var result = Result<int>.Success(10);

        var output = result.Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"fail:{e.Message}");

        Assert.Equal("ok:10", output);
    }

    [Fact]
    public void Match_OnFailure_InvokesOnFailureFunc()
    {
        var result = Result<int>.Failure(ErrorKind.IoError, "disk");

        var output = result.Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"fail:{e.Message}");

        Assert.Equal("fail:disk", output);
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Success(5);

        var mapped = result.Map(v => v * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(10, mapped.Value);
    }

    [Fact]
    public void Map_OnFailure_PreservesError()
    {
        var result = Result<int>.Failure(ErrorKind.Validation, "bad");

        var mapped = result.Map(v => v * 2);

        Assert.True(mapped.IsFailure);
        Assert.Equal(ErrorKind.Validation, mapped.Error.Kind);
        Assert.Equal("bad", mapped.Error.Message);
    }

    [Fact]
    public void Bind_OnSuccess_ChainsToNextResult()
    {
        var result = Result<int>.Success(3);

        var bound = result.Bind(v => Result<string>.Success($"val:{v}"));

        Assert.True(bound.IsSuccess);
        Assert.Equal("val:3", bound.Value);
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuitsWithOriginalError()
    {
        var result = Result<int>.Failure(ErrorKind.InvalidConfiguration, "oops");

        var bound = result.Bind(v => Result<string>.Success($"val:{v}"));

        Assert.True(bound.IsFailure);
        Assert.Equal(ErrorKind.InvalidConfiguration, bound.Error.Kind);
        Assert.Equal("oops", bound.Error.Message);
    }

    [Fact]
    public void Bind_OnSuccess_CanReturnFailure()
    {
        var result = Result<int>.Success(0);

        var bound = result.Bind(v =>
            v == 0
                ? Result<string>.Failure(ErrorKind.Validation, "zero not allowed")
                : Result<string>.Success($"val:{v}"));

        Assert.True(bound.IsFailure);
        Assert.Equal("zero not allowed", bound.Error.Message);
    }

    [Fact]
    public void Error_ToString_ContainsKindAndMessage()
    {
        var error = new Error(ErrorKind.SecurityError, "access denied");

        Assert.Equal("SecurityError: access denied", error.ToString());
    }

    [Fact]
    public void GetValueOrDefault_OnSuccess_ReturnsValue()
    {
        var result = Result<int>.Success(42);

        Assert.Equal(42, result.GetValueOrDefault());
        Assert.Equal(42, result.GetValueOrDefault(-1));
    }

    [Fact]
    public void GetValueOrDefault_OnFailure_ReturnsDefaultOfT()
    {
        var result = Result<int>.Failure(ErrorKind.Validation, "bad");

        Assert.Equal(0, result.GetValueOrDefault());
    }

    [Fact]
    public void GetValueOrDefault_WithFallback_OnFailure_ReturnsFallback()
    {
        var result = Result<int>.Failure(ErrorKind.Validation, "bad");

        Assert.Equal(-1, result.GetValueOrDefault(-1));
    }

    [Fact]
    public void GetValueOrDefault_OnSuccessCarryingNull_ReturnsNull()
    {
        var result = Result<string?>.Success(null);

        Assert.Null(result.GetValueOrDefault());
    }

    [Fact]
    public void GetValueOrDefault_WithFallback_OnSuccessCarryingNull_ReturnsNullNotFallback()
    {
        // The operation succeeded, so the stored null must win over the fallback — mirrors
        // Dictionary.GetValueOrDefault, which substitutes only when the key is absent, not when
        // the stored value happens to be null.
        var result = Result<string?>.Success(null);

        var value = result.GetValueOrDefault("fallback");

        Assert.Null(value);
    }

    [Fact]
    public void TryGetValue_OnSuccess_ReturnsTrueAndValue()
    {
        var result = Result<int>.Success(7);

        var found = result.TryGetValue(out var value);

        Assert.True(found);
        Assert.Equal(7, value);
    }

    [Fact]
    public void TryGetValue_OnFailure_ReturnsFalseAndDefault()
    {
        var result = Result<int>.Failure(ErrorKind.Validation, "bad");

        var found = result.TryGetValue(out var value);

        Assert.False(found);
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryGetValue_OnSuccessCarryingNull_DistinguishesFromFailure_UnlikeGetValueOrDefault()
    {
        // This is the reason TryGetValue exists: GetValueOrDefault() cannot tell "failed" apart from
        // "succeeded with null" (both return null). TryGetValue can — it returns true here.
        var result = Result<string?>.Success(null);

        Assert.Null(result.GetValueOrDefault());

        var found = result.TryGetValue(out var value);

        Assert.True(found);
        Assert.Null(value);
    }
}
