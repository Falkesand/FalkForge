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
        return value is null ? throw new ArgumentNullException(nameof(value)) : new Result<T>(value);
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
}