using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace VisuAuth.EntraCore.Infrastructure;

/// <summary>
/// Removes a directory user's registered authentication methods through
/// Microsoft Graph so the user must re-enrol their second factor. Shared
/// by both Entra adapter families (Workforce <c>VisuAuth.Entra</c> and
/// customer-facing <c>VisuAuth.EntraExternal</c>) because the Graph
/// surface is identical — only the store that wraps the result differs.
/// </summary>
/// <remarks>
/// <para>
/// Graph has no single "reset MFA" call. The user's
/// <c>/authentication/methods</c> collection is polymorphic, and each
/// concrete method type is deleted through its own typed endpoint
/// (<c>/authentication/microsoftAuthenticatorMethods/{id}</c>,
/// <c>/fido2Methods/{id}</c>, …). This helper lists the methods once and
/// dispatches a DELETE per deletable item.
/// </para>
/// <para>
/// <b>Password is never touched.</b> The
/// <c>passwordAuthenticationMethod</c> can't be deleted (it's the account
/// itself), and method types Graph doesn't let us remove are skipped
/// rather than erroring — the goal is "drop the second factors", not
/// "delete the only credential". <see cref="ODataError"/> propagates to
/// the caller so each store maps it to its own
/// <c>StoreResult</c> shape (404 → "user not found", etc.).
/// </para>
/// </remarks>
public static class EntraTwoFactorReset
{
    /// <summary>
    /// Lists the user's authentication methods and deletes every
    /// removable second factor. Returns the number of methods deleted.
    /// Lets <see cref="ODataError"/> bubble so the caller can translate it.
    /// </summary>
    public static async Task<int> RemoveAllAsync(
        GraphServiceClient graph,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var response = await graph.Users[userId].Authentication.Methods
            .GetAsync(cancellationToken: cancellationToken);

        var methods = response?.Value ?? [];
        var deleted = 0;
        foreach (var method in methods)
        {
            if (method.Id is null)
            {
                continue;
            }

            // Pattern-match on the concrete polymorphic type → typed DELETE
            // builder. Password is intentionally absent (undeletable); any
            // future / unknown method type falls through the default and is
            // left alone rather than failing the whole reset.
            switch (method)
            {
                case MicrosoftAuthenticatorAuthenticationMethod:
                    await graph.Users[userId].Authentication.MicrosoftAuthenticatorMethods[method.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                    deleted++;
                    break;
                case Fido2AuthenticationMethod:
                    await graph.Users[userId].Authentication.Fido2Methods[method.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                    deleted++;
                    break;
                case PhoneAuthenticationMethod:
                    await graph.Users[userId].Authentication.PhoneMethods[method.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                    deleted++;
                    break;
                case SoftwareOathAuthenticationMethod:
                    await graph.Users[userId].Authentication.SoftwareOathMethods[method.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                    deleted++;
                    break;
                case WindowsHelloForBusinessAuthenticationMethod:
                    await graph.Users[userId].Authentication.WindowsHelloForBusinessMethods[method.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                    deleted++;
                    break;
                case EmailAuthenticationMethod:
                    await graph.Users[userId].Authentication.EmailMethods[method.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                    deleted++;
                    break;
                default:
                    // passwordAuthenticationMethod + any type we can't
                    // delete via a typed endpoint — leave it in place.
                    break;
            }
        }

        return deleted;
    }
}
