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

    public static UserResult Success(string? userId = null) => new()
    {
        IsSuccess = true,
        UserId = userId,
    };

    public static UserResult Failure(string error, IReadOnlyList<string>? validationErrors = null) => new()
    {
        IsSuccess = false,
        Error = error,
        ValidationErrors = validationErrors ?? [],
    };
}
