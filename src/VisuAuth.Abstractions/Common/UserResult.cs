namespace VisuAuth.Abstractions.Common;

/// <summary>
/// Outcome of a user-store operation. Avoids exceptions for expected business errors
/// (validation, conflicts, missing records).
/// </summary>
public sealed record UserResult
{
    public required bool IsSuccess { get; init; }

    public string? UserId { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    /// <summary>
    /// Free-form payload returned by operations that need to surface extra data
    /// (e.g. a generated temporary password from <c>ResetPasswordAsync</c>, a
    /// password-reset URL, a fresh authenticator key). Keys are documented per
    /// operation in the matching method on <c>IUserStore</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    public static UserResult Success(
        string? userId = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
    {
        IsSuccess = true,
        UserId = userId,
        Metadata = metadata ?? new Dictionary<string, string>(),
    };

    public static UserResult Failure(string error, IReadOnlyList<string>? validationErrors = null) => new()
    {
        IsSuccess = false,
        Error = error,
        ValidationErrors = validationErrors ?? [],
    };
}
