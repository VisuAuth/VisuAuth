namespace VisuAuth.EndUserUi.Api;

/// <summary>Body of <c>POST /visuauth/api/auth/login</c>.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Body of <c>POST /visuauth/api/auth/register</c>.</summary>
public sealed record RegisterRequest(string Email, string Password);

/// <summary>
/// Body of <c>POST /visuauth/api/auth/refresh</c> once the refresh-token
/// plugin is enabled. Without the plugin the endpoint reads the (possibly
/// expired) access token from the <c>Authorization</c> header instead and this
/// body is not used.
/// </summary>
public sealed record RefreshRequest(string? RefreshToken);

/// <summary>Success payload returned by every successful auth endpoint.</summary>
/// <param name="RefreshToken">
/// The opaque refresh token to store and present at <c>/refresh</c>. Only
/// populated when the refresh-token plugin is enabled; <c>null</c> otherwise.
/// It is single-use — each refresh returns a replacement.
/// </param>
public sealed record AuthSuccessResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string UserId,
    string Email,
    string? TenantId,
    string? RefreshToken = null);

/// <summary>Failure payload — never leaks whether the user exists.</summary>
public sealed record AuthErrorResponse(string Error, IReadOnlyList<string>? Details = null);
