namespace VisuAuth.Abstractions.Users;

/// <summary>
/// Optional backend service that supplies the email domains the admin
/// Create-User form should offer. When registered AND it returns two or
/// more domains, the form renders a domain dropdown (type the local part,
/// pick the domain); with one domain it behaves like the fixed
/// <see cref="VisuAuth.Abstractions.Capabilities.UserBackendCapabilities.EmailDomainSuffix"/>
/// suffix; with none (or no registration) the form falls back to a
/// free-text email input.
/// </summary>
/// <remarks>
/// <para>
/// Exists so the Microsoft Entra adapter can surface a tenant's
/// <i>verified</i> domains (discovered from Graph <c>/domains</c>) instead
/// of the single configured default — a multi-domain tenant can then pick
/// which verified domain a new user's UPN belongs to, rather than being
/// locked to one suffix.
/// </para>
/// <para>
/// Optional by design: the page resolves it with a null default, so
/// backends that don't implement it (ASP.NET Core Identity, which accepts
/// any email) simply keep the free-text input. Implementations should
/// cache — the admin form calls this on every render.
/// </para>
/// </remarks>
public interface IEmailDomainSource
{
    /// <summary>
    /// Returns the email domains to offer, without a leading <c>@</c>,
    /// ordered with the tenant's primary/default domain first. Empty when
    /// the backend has nothing to suggest (the form then uses free text).
    /// Implementations degrade to an empty list on backend errors rather
    /// than throwing — a domain-discovery failure must not break the
    /// Create-User page.
    /// </summary>
    Task<IReadOnlyList<string>> GetEmailDomainsAsync(CancellationToken cancellationToken = default);
}
