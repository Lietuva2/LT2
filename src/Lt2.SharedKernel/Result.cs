namespace Lt2.SharedKernel;

/// <summary>Kind of failure, mapped to an HTTP status by the web host.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    TooManyRequests,
}

/// <summary>A failure with a stable machine-readable code and a localisation key for the message.</summary>
public sealed record Error(ErrorKind Kind, string Code, string? Field = null)
{
    public static Error Validation(string code, string? field = null) => new(ErrorKind.Validation, code, field);
    public static Error NotFound(string code) => new(ErrorKind.NotFound, code);
    public static Error Conflict(string code) => new(ErrorKind.Conflict, code);
    public static Error Forbidden(string code) => new(ErrorKind.Forbidden, code);
    public static Error Unauthorized(string code) => new(ErrorKind.Unauthorized, code);
}

public readonly record struct Unit
{
    public static Unit Value => default;
}

/// <summary>Outcome of an application operation: a value or an error, never both.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        Error = error;
    }

    public bool IsSuccess { get; }

    public Error? Error { get; }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}

public static class Result
{
    public static Result<Unit> Ok() => Result<Unit>.Success(Unit.Value);
}
