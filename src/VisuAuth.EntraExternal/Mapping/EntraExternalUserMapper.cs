using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Users;
using GraphUser = Microsoft.Graph.Models.User;

namespace VisuAuth.EntraExternal.Mapping;

/// <summary>
/// Pure projections from Microsoft Graph user resources to VisuAuth DTOs,
/// in the shape External ID requires. Static so the conversions can be
/// unit-tested without a <see cref="Microsoft.Graph.GraphServiceClient"/>
/// instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists separate from <c>VisuAuth.Entra.Mapping.EntraUserMapper</c>.</b>
/// The Workforce mapper persists user identity as <c>userPrincipalName</c>
/// on a verified tenant domain. External ID instead carries identity as
/// an <c>identities[]</c> array — one entry per sign-in method (local
/// email account, federated social provider, etc.) — and a Microsoft-
/// generated UPN that callers normally don't touch. Forking the mapper
/// keeps each adapter focused on the shape its tenant family expects,
/// rather than smuggling a backend flag through the shared code (which
/// would violate CLAUDE.md §2.5: adapters stay independent).
/// </para>
/// <para>
/// <b>What we map in common with Workforce:</b> read-side projections to
/// <see cref="UserSummary"/> / <see cref="UserDetail"/>, the filter
/// builder, and the update payload (DisplayName + BusinessPhones — UPN /
/// mail patches are deliberately omitted, same reasoning as the Workforce
/// mapper).
/// </para>
/// </remarks>
internal static class EntraExternalUserMapper
{
    /// <summary>
    /// Comma-separated list of Graph user properties the store should
    /// $select when listing users. Same shape as the Workforce mapper —
    /// the read surface is identical, only the create-payload differs
    /// between tenant families.
    /// </summary>
    /// <remarks>
    /// <b>Why no signInActivity?</b> Same constraint as Workforce: that
    /// field needs <c>AuditLog.Read.All</c> + Entra ID P1+, which most
    /// External tenants don't carry. Leaving it out keeps the list call
    /// working on free tenants; admin UI degrades to "—" on the column.
    /// </remarks>
    public const string SummarySelect =
        "id,displayName,userPrincipalName,mail,identities,businessPhones,accountEnabled,createdDateTime";

    /// <summary>
    /// Wider $select for the detail page — adds the profile fields
    /// <see cref="UserDetail"/> exposes that the summary doesn't.
    /// </summary>
    public const string DetailSelect = SummarySelect + ",givenName,surname,jobTitle,department";

    public static UserSummary ToSummary(GraphUser source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var email = ResolveEmail(source);
        return new UserSummary
        {
            Id = source.Id ?? string.Empty,
            Email = email,
            // External users get a Microsoft-generated UPN that's ugly
            // (e.g. cpim_xxxxxxxxxxxx@{tenant}.onmicrosoft.com). The
            // display-friendly value is the identities[] issuerAssignedId
            // (i.e. the email the customer typed). We surface that as the
            // user name too so the admin list shows something readable.
            UserName = email,
            PhoneNumber = FirstPhone(source.BusinessPhones),
            IsEnabled = source.AccountEnabled ?? true,
            EmailConfirmed = !string.IsNullOrEmpty(email),
            // 2FA enrolment isn't on the User entity; a separate /authentication
            // /methods call would be an N+1 against Graph for a list view.
            // UserDetail can fill this in via that call when SupportsTwoFactor
            // is on (it isn't, so it stays false here too).
            TwoFactorEnabled = false,
            LockoutEnd = null,
            // External ID has a single tenant id at the directory level
            // (configured in EntraExternalOptions). Per-user tenancy is
            // a multi-tenant SaaS concept that doesn't apply to a single
            // External directory.
            TenantId = null,
            CreatedAt = source.CreatedDateTime ?? DateTimeOffset.MinValue,
            LastSignInAt = source.SignInActivity?.LastSignInDateTime,
        };
    }

    public static UserDetail ToDetail(GraphUser source, IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(roles);
        var email = ResolveEmail(source);
        return new UserDetail
        {
            Id = source.Id ?? string.Empty,
            Email = email,
            UserName = email,
            PhoneNumber = FirstPhone(source.BusinessPhones),
            EmailConfirmed = !string.IsNullOrEmpty(email),
            PhoneNumberConfirmed = source.BusinessPhones is { Count: > 0 },
            IsEnabled = source.AccountEnabled ?? true,
            TwoFactorEnabled = false,
            LockoutEnabled = false,
            LockoutEnd = null,
            AccessFailedCount = 0,
            TenantId = null,
            CreatedAt = source.CreatedDateTime ?? DateTimeOffset.MinValue,
            LastSignInAt = source.SignInActivity?.LastSignInDateTime,
            Roles = roles,
            // v0.3 doesn't surface Graph extension properties / user
            // attributes yet (the External-specific signup-collected
            // fields). SupportsCustomClaims = true reserves the slot.
            Claims = [],
            // External ID supports federated identities — those would land
            // here in v0.4+. v0.3 admin UI doesn't render this section yet.
            ExternalLogins = [],
        };
    }

    /// <summary>
    /// Builds the <see cref="GraphUser"/> Graph wants for a new customer
    /// account in an External tenant. Materially different from the
    /// Workforce mapper: identity travels through the
    /// <c>identities[]</c> array (signInType / issuer / issuerAssignedId)
    /// rather than <c>userPrincipalName</c>. Microsoft auto-generates a
    /// UPN of the shape <c>cpim_{guid}@{tenant}.onmicrosoft.com</c> on
    /// the server side.
    /// </summary>
    public static (GraphUser graphUser, string temporaryPassword) ToGraphCreate(
        CreateUserCommand command,
        string tenantDomain,
        Func<string> temporaryPasswordFactory)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantDomain);
        ArgumentNullException.ThrowIfNull(temporaryPasswordFactory);

        var temporaryPassword = string.IsNullOrEmpty(command.Password)
            ? temporaryPasswordFactory()
            : command.Password;

        // mailNickname still required by Graph for /users POST regardless
        // of tenant family — fall back to the local-part of the email.
        var atIndex = command.Email.IndexOf('@');
        var nickname = atIndex > 0 ? command.Email[..atIndex] : command.Email;

        // businessPhones rejects an explicit null on the wire (the Graph
        // schema marks the collection as [Nullable=False]). Empty list is
        // the canonical "no phone" payload. Same reasoning as Workforce.
        List<string> phones = string.IsNullOrEmpty(command.PhoneNumber)
            ? []
            : [command.PhoneNumber];

        return (new GraphUser
        {
            AccountEnabled = true,
            DisplayName = command.UserName ?? command.Email,
            MailNickname = nickname,
            // The External-specific bit: identity travels here, not in
            // UserPrincipalName. SignInType "emailAddress" means this is
            // a local-account credential whose password lives in the
            // directory; federated entries would use the provider name as
            // the SignInType value (Google, Facebook, Apple, etc.) and
            // the social subject id as IssuerAssignedId.
            Identities =
            [
                new ObjectIdentity
                {
                    SignInType = "emailAddress",
                    Issuer = tenantDomain,
                    IssuerAssignedId = command.Email,
                },
            ],
            BusinessPhones = phones,
            PasswordProfile = new PasswordProfile
            {
                Password = temporaryPassword,
                // Same convention as the Workforce mapper — hand the temp
                // password to the admin once, force the user to set their
                // own on first sign-in. Only forced when WE generated the
                // password; if the caller provided one, treat it as final.
                ForceChangePasswordNextSignIn = string.IsNullOrEmpty(command.Password),
            },
        }, temporaryPassword);
    }

    /// <summary>
    /// Translates an <see cref="UpdateUserCommand"/> into the PATCH body
    /// Graph expects. Same partial-update semantics as the Workforce
    /// mapper: null command fields stay null on the payload (Graph PATCH
    /// treats absent properties as "leave unchanged").
    /// </summary>
    /// <remarks>
    /// <b>identities[] / UPN / mail are intentionally NOT patched.</b>
    /// For External users the identities[] entry IS the login credential
    /// — rewriting it from a generic admin form would lock the customer
    /// out of their own account (an audit-grade event we shouldn't make
    /// trivially possible). Email-change requests should go through a
    /// dedicated flow (verification mail + the customer confirms) that
    /// v0.4+ may surface; for now, generic admin updates touch the safe
    /// surface — display name + phones.
    /// </remarks>
    public static GraphUser ToGraphUpdate(UpdateUserCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var patch = new GraphUser();
        if (!string.IsNullOrEmpty(command.UserName))
        {
            patch.DisplayName = command.UserName;
        }
        if (command.PhoneNumber is not null)
        {
            patch.BusinessPhones = string.IsNullOrWhiteSpace(command.PhoneNumber)
                ? []
                : [command.PhoneNumber];
        }
        return patch;
    }

    /// <summary>
    /// Builds the <c>$filter</c> clause for the Graph users list. Returns
    /// null when the input has no usable filter (Graph rejects an empty
    /// string). Mirrors the Workforce mapper's strategy: <c>startswith</c>
    /// on the most-common columns, the cheapest predicate Entra indexes.
    /// External adds the identities[] sub-filter so a search for an email
    /// the customer typed during signup hits the right row even when
    /// UPN / mail diverge from it.
    /// </summary>
    public static string? BuildGraphFilter(UserFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var clauses = new List<string>(3);

        if (filter.IsEnabled is { } enabled)
        {
            clauses.Add($"accountEnabled eq {enabled.ToString().ToLowerInvariant()}");
        }
        if (filter.IsLockedOut is { } lockedOut)
        {
            // VisuAuth's "locked" maps to Entra's accountEnabled = false.
            clauses.Add($"accountEnabled eq {(!lockedOut).ToString().ToLowerInvariant()}");
        }
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var needle = EscapeForFilter(filter.SearchTerm.Trim());
            // identities/any(…) lets the search hit the customer-typed
            // email even when the server-generated UPN doesn't match. The
            // ConsistencyLevel: eventual header (set by the store) is
            // required for any predicate against the identities collection.
            clauses.Add(
                $"(startswith(displayName,'{needle}') " +
                $"or startswith(userPrincipalName,'{needle}') " +
                $"or startswith(mail,'{needle}') " +
                $"or identities/any(id:id/issuerAssignedId eq '{needle}'))");
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    /// <summary>
    /// Resolves the "human-friendly" email for an External user: prefers
    /// the identities[] entry the customer actually typed (signInType =
    /// emailAddress), falls back to mail, then to UPN. The auto-generated
    /// External UPN (<c>cpim_{guid}@...</c>) is the last resort because
    /// surfacing it in the admin grid is actively confusing — operators
    /// expect "the email the customer registered with".
    /// </summary>
    private static string ResolveEmail(GraphUser source)
    {
        var emailIdentity = source.Identities?.FirstOrDefault(i =>
            string.Equals(i.SignInType, "emailAddress", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(i.IssuerAssignedId));
        if (emailIdentity is not null)
        {
            return emailIdentity.IssuerAssignedId!;
        }
        return source.Mail ?? source.UserPrincipalName ?? string.Empty;
    }

    private static string? FirstPhone(List<string>? phones)
        => phones is { Count: > 0 } ? phones[0] : null;

    /// <summary>
    /// OData $filter literals escape a single quote by doubling it.
    /// Customer-typed search terms come from end-user input.
    /// </summary>
    private static string EscapeForFilter(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
