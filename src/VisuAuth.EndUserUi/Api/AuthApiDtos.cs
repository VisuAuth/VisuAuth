namespace VisuAuth.EndUserUi.Api;

/// <summary>Body of <c>POST /visuauth/api/auth/login</c>.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Body of <c>POST /visuauth/api/auth/register</c>.</summary>
public sealed record RegisterRequest(string Email, string Password);

/// <summary>Success payload returned by every successful auth endpoint.</summary>
public sealed record AuthSuccessResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string UserId,
    string Email,
    string? TenantId);

/// <summary>Failure payload — never leaks whether the user exists.</summary>
public sealed record AuthErrorResponse(string Error, IReadOnlyList<string>? Details = null);
