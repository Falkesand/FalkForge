namespace FalkForge;

public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value) { _value = value; _error = null; }
    private Result(Error error) { _value = default; _error = error; }

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Cannot access Value on failed result: {_error}");
    public Error Error => IsFailure ? _error!.Value : throw new InvalidOperationException("Cannot access Error on successful result");

    public static Result<T> Success(T value) =>
        value is null ? throw new ArgumentNullException(nameof(value)) : new(value);
    public static Result<T> Failure(Error error) => new(error);
    public static Result<T> Failure(ErrorKind kind, string message) => new(new Error(kind, message));

    public static implicit operator Result<T>(T value) => Success(value);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!.Value);

    public Result<TResult> Map<TResult>(Func<T, TResult> map) =>
        IsSuccess ? Result<TResult>.Success(map(_value!)) : Result<TResult>.Failure(_error!.Value);

    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bind) =>
        IsSuccess ? bind(_value!) : Result<TResult>.Failure(_error!.Value);
}
