namespace VisuAuth.Abstractions.Common;

/// <summary>
/// Outcome of a store / flow operation (user, role, tenant, two-factor,
/// adapter-config, external-provider). Avoids exceptions for expected business
/// errors (validation, conflicts, missing records) — callers branch on
/// <see cref="IsSuccess"/> instead of catching.
/// </summary>
public sealed record StoreResult
{
    /// <summary>True when the operation completed successfully.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Identifier of the resource the operation acted on, when relevant — e.g.
    /// the new user id from <c>CreateAsync</c>, or the affected role / tenant id.
    /// <see langword="null"/> when the operation has no single resource id.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>Human-readable error message when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? Error { get; init; }

    /// <summary>Per-field validation messages for a failed operation; empty otherwise.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    /// <summary>
    /// Free-form payload returned by operations that need to surface extra data
    /// (e.g. a generated temporary password from <c>ResetPasswordAsync</c>, a
    /// password-reset URL, a fresh authenticator key). Keys are documented per
    /// operation on the matching store / flow method.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Creates a successful result, optionally carrying a resource id and metadata.</summary>
    public static StoreResult Success(
        string? resourceId = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
    {
        IsSuccess = true,
        ResourceId = resourceId,
        Metadata = metadata ?? new Dictionary<string, string>(),
    };

    /// <summary>Creates a failed result with an error message and optional validation messages.</summary>
    public static StoreResult Failure(string error, IReadOnlyList<string>? validationErrors = null) => new()
    {
        IsSuccess = false,
        Error = error,
        ValidationErrors = validationErrors ?? [],
    };
}
