namespace IntelliTrader.Application.Common;

/// <summary>
/// Represents the result of an operation that can either succeed or fail.
/// </summary>
/// <typeparam name="T">The type of the value on success</typeparam>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of a failed result");

    public Error Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access error of a successful result");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);
    public static Result<T> Failure(string code, string message) => new(new Error(code, message));

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    public async Task<TResult> MatchAsync<TResult>(
        Func<T, Task<TResult>> onSuccess,
        Func<Error, Task<TResult>> onFailure)
        => IsSuccess ? await onSuccess(_value!) : await onFailure(_error!);

    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
        => IsSuccess ? Result<TNew>.Success(mapper(_value!)) : Result<TNew>.Failure(_error!);

    public async Task<Result<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
        => IsSuccess ? Result<TNew>.Success(await mapper(_value!)) : Result<TNew>.Failure(_error!);

    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
        => IsSuccess ? binder(_value!) : Result<TNew>.Failure(_error!);

    public async Task<Result<TNew>> BindAsync<TNew>(Func<T, Task<Result<TNew>>> binder)
        => IsSuccess ? await binder(_value!) : Result<TNew>.Failure(_error!);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>
/// Represents the result of an operation that can either succeed or fail (without a value).
/// </summary>
public sealed class Result
{
    private readonly Error? _error;

    private Result()
    {
        IsSuccess = true;
        _error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public Error Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access error of a successful result");

    public static Result Success() => new();
    public static Result Failure(Error error) => new(error);
    public static Result Failure(string code, string message) => new(new Error(code, message));

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
        => IsSuccess ? onSuccess() : onFailure(_error!);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Represents an error with a code and message.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static Error None => new(string.Empty, string.Empty);
    public static Error NotFound(string entity, string id) => new("NotFound", $"{entity} with id '{id}' was not found");
    public static Error Validation(string message) => new("Validation", message);
    public static Error Conflict(string message) => new("Conflict", message);
    public static Error InsufficientFunds(string message) => new("InsufficientFunds", message);
    public static Error ExchangeError(string message) => new("ExchangeError", message);
    public static Error Unauthorized(string message) => new("Unauthorized", message);
    public static Error RateLimited(string message) => new("RateLimited", message);

    public override string ToString() => $"[{Code}] {Message}";
}
