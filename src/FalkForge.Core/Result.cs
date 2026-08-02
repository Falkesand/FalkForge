namespace FalkForge;

public readonly record struct Result<T>
{
    private readonly Error? _error;
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        _error = null;
    }

    private Result(Error error)
    {
        _value = default;
        _error = error;
    }

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value on failed result: {_error}");

    public Error Error => IsFailure
        ? _error!.Value
        : throw new InvalidOperationException("Cannot access Error on successful result");

    public static Result<T> Success(T value)
    {
        return new Result<T>(value);
    }

    public static Result<T> Failure(Error error)
    {
        return new Result<T>(error);
    }

    public static Result<T> Failure(ErrorKind kind, string message)
    {
        return new Result<T>(new Error(kind, message));
    }

    public static implicit operator Result<T>(T value)
    {
        return Success(value);
    }

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(_value!) : onFailure(_error!.Value);
    }

    public Result<TResult> Map<TResult>(Func<T, TResult> map)
    {
        return IsSuccess ? Result<TResult>.Success(map(_value!)) : Result<TResult>.Failure(_error!.Value);
    }

    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bind)
    {
        return IsSuccess ? bind(_value!) : Result<TResult>.Failure(_error!.Value);
    }

    /// <summary>
    /// Returns the success value — including a legitimate null carried by a success result — or
    /// <c>default</c> if this result is a failure. This cannot distinguish "failed" from "succeeded
    /// with null"; both return null/default. Use <see cref="TryGetValue"/> when that distinction
    /// matters.
    /// </summary>
    public T? GetValueOrDefault()
    {
        return IsSuccess ? _value : default;
    }

    /// <summary>
    /// Returns the success value — including a legitimate null carried by a success result, which is
    /// never replaced by <paramref name="fallback"/> — or <paramref name="fallback"/> if this result
    /// is a failure. Mirrors the BCL <c>Dictionary.GetValueOrDefault</c> convention: the fallback
    /// substitutes only when the operation failed (key absent), never when the stored value happens
    /// to be null.
    /// </summary>
    public T GetValueOrDefault(T fallback)
    {
        return IsSuccess ? _value! : fallback;
    }

    /// <summary>
    /// Attempts to get the success value, distinguishing "failed" from "succeeded with null" — the
    /// gap <see cref="GetValueOrDefault()"/> cannot cover. Returns <c>true</c> and sets
    /// <paramref name="value"/> to the stored value (which may legitimately be null) when this
    /// result is a success; returns <c>false</c> and sets <paramref name="value"/> to <c>default</c>
    /// otherwise. <paramref name="value"/> is deliberately annotated <c>T?</c> rather than
    /// <c>[MaybeNullWhen(false)] T</c>: a null value is possible even when this returns <c>true</c>,
    /// so promising non-null on success would be a false guarantee to callers.
    /// </summary>
    public bool TryGetValue(out T? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess;
    }
}