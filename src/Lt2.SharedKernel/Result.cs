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

    /// <summary>Removed on purpose and not coming back (410): a retired public profile, design 8.25.1.</summary>
    Gone,
}

/// <summary>A failure with a stable machine-readable code and a localisation key for the message.</summary>
public sealed record Error(ErrorKind Kind, string Code, string? Field = null)
{
    /// <summary>
    /// Whole numbers the message names, in order (<c>{0}</c>, <c>{1}</c> of the text), for a rule whose number is a setting:
    /// the text then says the number that holds, not a figure written into it. Empty for most errors.
    /// </summary>
    public IReadOnlyList<int> Numbers { get; init; } = Array.Empty<int>();

    /// <summary>
    /// The message's key when it says more than the code (design 8.23.3: a refused comment's message names what was found);
    /// null for the code's own message.
    /// </summary>
    public string? MessageKey { get; init; }

    /// <summary>
    /// Every refusal of a form saved whole or not at all (design 8.28.5), each with its <see cref="Field"/>; empty for most errors.
    /// The answer names every refused field with its own message.
    /// </summary>
    public IReadOnlyList<Error> Refusals { get; init; } = Array.Empty<Error>();

    /// <summary>
    /// The refusals of one form as one error: a single refusal stays itself (its own status and code), several are a
    /// <c>validation</c> error that carries them all in <see cref="Refusals"/>.
    /// </summary>
    public static Error All(IReadOnlyList<Error> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);
        if (refusals.Count == 0)
        {
            throw new ArgumentException("A refusal needs at least one reason.", nameof(refusals));
        }

        return refusals.Count == 1 ? refusals[0] : new Error(ErrorKind.Validation, "validation") { Refusals = refusals };
    }

    public static Error Validation(string code, string? field = null) => new(ErrorKind.Validation, code, field);
    public static Error NotFound(string code) => new(ErrorKind.NotFound, code);
    public static Error Conflict(string code) => new(ErrorKind.Conflict, code);
    public static Error Forbidden(string code) => new(ErrorKind.Forbidden, code);
    public static Error Unauthorized(string code) => new(ErrorKind.Unauthorized, code);

    public static Error Gone(string code) => new(ErrorKind.Gone, code);
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
