namespace Lt2.SharedKernel;

/// <summary>
/// A staff member's statement of reasons (design 8.3: every moderation action says why): required where the action needs
/// one, at most <see cref="MaxLength"/> characters. Checked by the module that stores it (design 6.8).
/// </summary>
public static class StaffStatements
{
    public const int MaxLength = 1000;

    /// <summary>
    /// <c>statement.required</c>, <c>staff-statement.too-long</c> (its own code: an organisation's statement on a bill,
    /// <c>statement.too-long</c>, has another limit), or null when the statement may be stored.
    /// </summary>
    public static Error? Check(string? statement, bool required = true) =>
        string.IsNullOrWhiteSpace(statement) ? (required ? Error.Validation("statement.required", "statement") : null)
        : statement.Trim().Length > MaxLength ? Error.Validation("staff-statement.too-long", "statement")
        : null;
}
