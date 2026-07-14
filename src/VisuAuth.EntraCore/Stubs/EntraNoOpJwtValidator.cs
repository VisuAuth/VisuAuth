using VisuAuth.Abstractions.Authentication;

namespace VisuAuth.EntraCore.Stubs;

/// <summary>
/// Stub <see cref="IJwtValidator"/> that the Entra adapter family registers
/// so the minimal-API <c>/visuauth/api/auth/refresh</c> endpoint (which
/// lists <see cref="IJwtValidator"/> as a required dependency) can still be
/// mapped at startup even when no HS256 issuer / validator is wired.
/// </summary>
/// <remarks>
/// In Entra mode the JWT-issued mobile flow is satisfied by Microsoft's own
/// tokens, so VisuAuth's HS256 validator has nothing to do.
/// <see cref="ValidateSignatureAndReadSubject"/> always returns <c>null</c> —
/// the <c>AuthApi</c> refresh branch treats that as an unauthenticated token
/// and surfaces a clean 401.
/// </remarks>
public sealed class EntraNoOpJwtValidator : IJwtValidator
{
    public string? ValidateSignatureAndReadSubject(string token) => null;
}
