using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace VisuAuth.EndUserUi.Authentication;

/// <summary>
/// Marks every page in a VisuAuth UI assembly that does <b>not</b> declare
/// <see cref="AuthorizeAttribute"/> as explicitly anonymous, so a consumer's
/// global <see cref="AuthorizationOptions.FallbackPolicy"/> cannot lock the
/// pages users need in order to authenticate in the first place.
/// </summary>
/// <remarks>
/// <para>
/// "Require an authenticated user for everything unless opted out" is a common
/// and otherwise sensible hardening move:
/// </para>
/// <code>
/// options.FallbackPolicy = new AuthorizationPolicyBuilder()
///     .RequireAuthenticatedUser().Build();
/// </code>
/// <para>
/// A fallback policy applies to any endpoint carrying no authorization metadata
/// of its own. Sign-in pages carry none by default, so the fallback catches them
/// too — and the result is a deadlock: <c>/visuauth/login</c> challenges, which
/// redirects to <c>/visuauth/login</c>, which challenges again. The API is worse
/// in a quieter way: <c>POST /visuauth/api/auth/login</c> simply answers 401, so
/// no one can obtain a token.
/// </para>
/// <para>
/// This convention does not <em>change</em> any page's intended access level. It
/// records the level the page already had, as explicit metadata, so a fallback
/// cannot silently override it. Pages that deliberately require a signed-in user
/// (two-factor setup, recovery codes) declare <see cref="AuthorizeAttribute"/>
/// and are left untouched — including the admin dashboard, which is gated by its
/// own policy and lives in a different assembly.
/// </para>
/// <para>
/// Note that the two-factor <em>challenge</em> page is deliberately anonymous:
/// it runs mid-sign-in, before the user holds a full identity, so requiring one
/// would break the very flow it completes.
/// </para>
/// </remarks>
public sealed class PreserveAnonymousAccessConvention(Assembly visuauthAssembly)
    : IPageApplicationModelConvention
{
    /// <summary>
    /// Lets idempotent registration paths detect a convention already targeting
    /// this assembly without comparing private state.
    /// </summary>
    public bool OwnsAssembly(Assembly assembly) => visuauthAssembly == assembly;

    public void Apply(PageApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // PageApplicationModel exposes the compiled page type directly, so the
        // assembly check needs no Properties-bag lookup.
        if (model.HandlerType.Assembly != visuauthAssembly)
        {
            return;
        }

        // The page asked for authorization — honour that and stay out of the way.
        if (model.Filters.OfType<AuthorizeFilter>().Any()
            || model.HandlerTypeAttributes.OfType<IAuthorizeData>().Any()
            || model.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        // Already explicit (a consumer convention got here first).
        if (model.EndpointMetadata.OfType<IAllowAnonymous>().Any()
            || model.HandlerTypeAttributes.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        // EndpointMetadata, not Filters. The fallback policy is applied by the
        // authorization *middleware*, which runs ahead of MVC and only inspects
        // endpoint metadata — an AllowAnonymousFilter in the MVC filter pipeline
        // is invisible to it and the page would still be gated.
        model.EndpointMetadata.Add(new AllowAnonymousAttribute());
    }
}
