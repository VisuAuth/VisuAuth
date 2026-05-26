using Microsoft.Graph.Models;
using VisuAuth.Abstractions.Users;
using GraphUser = Microsoft.Graph.Models.User;

namespace VisuAuth.Entra.Mapping;

/// <summary>
/// Pure projections from Microsoft Graph user resources to VisuAuth DTOs.
/// Static so the conversions can be unit-tested without a
/// <see cref="Microsoft.Graph.GraphServiceClient"/> instance, and so the
/// store can call them inline without DI plumbing.
/// </summary>
/// <remarks>
/// <para>
/// Entra has no equivalent of "EmailConfirmed" as a separate boolean —
/// emails landing on the directory are validated server-side at the time
/// they're set, so we map <see cref="UserSummary.EmailConfirmed"/> to
/// "the user has an <c>UserPrincipalName</c>" (always true for a
/// well-formed Entra account). Same reasoning for
/// <see cref="UserSummary.PhoneNumberConfirmed"/> — Entra has
/// <c>businessPhones</c> as a free list; we report confirmed = true when
/// a number is present.
/// </para>
/// <para>
/// <c>LastSignInDateTime</c> lives under <c>SignInActivity</c> which
/// requires the <c>AuditLog.Read.All</c> permission AND a Microsoft Entra
/// ID P1 license. We map it best-effort — null when the property is
/// absent rather than failing the whole list call.
/// </para>
/// </remarks>
internal static class EntraUserMapper
{
    /// <summary>
    /// Comma-separated list of Graph User properties the store should
    /// $select when listing users. Keeping the projection narrow shrinks
    /// the wire payload and avoids accidental dependency on properties
    /// the registered app doesn't have permissions for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why no signInActivity?</b> That field is gated on the
    /// <c>AuditLog.Read.All</c> permission AND a Microsoft Entra ID P1+
    /// licence on the tenant. Free / E1 tenants reject the entire request
    /// (not just the field) with a 403 when it's in the select. Leaving
    /// it out means <see cref="UserSummary.LastSignInAt"/> stays null —
    /// the admin UI degrades to "—" in the column, which is the right
    /// behaviour when the data isn't available anyway. Consumers on P1+
    /// who want LastSignInAt populated can subclass / re-register the
    /// store with a custom select; v0.3 will turn this into an option.
    /// </para>
    /// </remarks>
    public const string SummarySelect =
        "id,displayName,userPrincipalName,mail,businessPhones,accountEnabled,createdDateTime";

    /// <summary>
    /// Wider $select for the detail page — adds anything the
    /// <see cref="UserDetail"/> projection exposes that the summary doesn't.
    /// </summary>
    public const string DetailSelect = SummarySelect + ",givenName,surname,jobTitle,department";

    public static UserSummary ToSummary(GraphUser source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new UserSummary
        {
            // Graph users are GUIDs; null is impossible in practice for a fetched
            // user, but the SDK types them as nullable so we coalesce defensively.
            Id = source.Id ?? string.Empty,
            // Email priority: mail (the published address) → UPN (which doubles
            // as login). For most workforce tenants UPN === mail.
            Email = source.Mail ?? source.UserPrincipalName ?? string.Empty,
            UserName = source.UserPrincipalName,
            PhoneNumber = FirstPhone(source.BusinessPhones),
            // Entra has no "disabled" vs "deleted" distinction in this field —
            // accountEnabled = false IS the lockout state for an Entra user.
            IsEnabled = source.AccountEnabled ?? true,
            EmailConfirmed = !string.IsNullOrEmpty(source.UserPrincipalName),
            // 2FA enrolment isn't on the User entity in Graph — it requires a
            // separate /authentication/methods call we deliberately skip for
            // list views (would be N+1 against Graph). UserDetail fills this
            // in via that call when SupportsTwoFactor is on (it isn't, in our
            // capability set, so it stays false here too).
            TwoFactorEnabled = false,
            LockoutEnd = null,
            // Entra workforce tenant has a single tenant id at the directory
            // level (configured in EntraOptions); per-user tenancy doesn't
            // apply. The Entra adapter ignores the VisuAuth TenantId filter
            // and surfaces null here.
            TenantId = null,
            CreatedAt = source.CreatedDateTime ?? DateTimeOffset.MinValue,
            LastSignInAt = source.SignInActivity?.LastSignInDateTime,
        };
    }

    public static UserDetail ToDetail(GraphUser source, IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(roles);
        return new UserDetail
        {
            Id = source.Id ?? string.Empty,
            Email = source.Mail ?? source.UserPrincipalName ?? string.Empty,
            UserName = source.UserPrincipalName,
            PhoneNumber = FirstPhone(source.BusinessPhones),
            EmailConfirmed = !string.IsNullOrEmpty(source.UserPrincipalName),
            PhoneNumberConfirmed = source.BusinessPhones is { Count: > 0 },
            IsEnabled = source.AccountEnabled ?? true,
            TwoFactorEnabled = false,
            // Entra's "smart lockout" is invisible to the API — surfacing
            // LockoutEnabled = false matches reality from the admin's POV
            // (you can't toggle a per-user lockout policy from outside).
            LockoutEnabled = false,
            LockoutEnd = null,
            AccessFailedCount = 0,
            TenantId = null,
            CreatedAt = source.CreatedDateTime ?? DateTimeOffset.MinValue,
            LastSignInAt = source.SignInActivity?.LastSignInDateTime,
            Roles = roles,
            // v0.2 doesn't surface Graph extension properties yet. v0.3
            // SupportsCustomClaims = true will hook this up.
            Claims = [],
            // Entra IS the external identity provider — listing it here
            // would be circular. Stays empty.
            ExternalLogins = [],
        };
    }

    /// <summary>
    /// Builds the <see cref="GraphUser"/> Graph wants for a new directory
    /// account. Returns the generated password too — the consumer wraps it
    /// into <see cref="VisuAuth.Abstractions.Common.UserResult.Metadata"/>
    /// so the admin UI can display the one-time password widget exactly
    /// like the Identity adapter does.
    /// </summary>
    public static (GraphUser graphUser, string temporaryPassword) ToGraphCreate(
        CreateUserCommand command,
        Func<string> temporaryPasswordFactory)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(temporaryPasswordFactory);

        var temporaryPassword = string.IsNullOrEmpty(command.Password)
            ? temporaryPasswordFactory()
            : command.Password;

        // mailNickname is the part before @ — Entra requires it. UPN must
        // be unique inside the tenant; we default to the email the caller
        // supplied (typical for SaaS onboarding).
        var atIndex = command.Email.IndexOf('@');
        var nickname = atIndex > 0 ? command.Email[..atIndex] : command.Email;

        // Graph types businessPhones as `Collection(Edm.String)[Nullable=False]`,
        // which rejects an explicit null on the wire even though it's "just
        // not setting it". Empty list is the canonical "no phones" payload —
        // Kiota serialises it as `[]`, which Graph accepts.
        var phones = string.IsNullOrEmpty(command.PhoneNumber)
            ? new List<string>()
            : new List<string> { command.PhoneNumber };

        return (new GraphUser
        {
            AccountEnabled = true,
            DisplayName = command.UserName ?? command.Email,
            MailNickname = nickname,
            UserPrincipalName = command.Email,
            BusinessPhones = phones,
            PasswordProfile = new PasswordProfile
            {
                Password = temporaryPassword,
                // Force a change on first sign-in matches the temporary-password
                // convention the Identity adapter uses — the admin hands the
                // password over once and the user picks their own immediately.
                ForceChangePasswordNextSignIn = string.IsNullOrEmpty(command.Password),
            },
        }, temporaryPassword);
    }

    /// <summary>
    /// Translates an <see cref="UpdateUserCommand"/> into the PATCH body
    /// Graph expects. Null command fields stay null on the graph payload —
    /// Graph PATCH semantics treat absent properties as "leave unchanged"
    /// (and explicit null as "clear"), so this preserves the partial-update
    /// behaviour the abstraction promises.
    /// </summary>
    /// <remarks>
    /// <b>UPN / mail are intentionally NOT patched.</b> Microsoft Graph
    /// rejects writes to <c>userPrincipalName</c> for B2B external users
    /// (the <c>{address}#EXT#@{tenant}</c> shape that lands when a guest
    /// is invited) with HTTP 403 "Insufficient privileges to complete the
    /// operation" — even when the calling app has User.ReadWrite.All. The
    /// <c>mail</c> property is server-managed too. Both are best changed
    /// in the Entra portal (or by a custom flow that handles the
    /// member-vs-guest branch); from the generic admin UI we only patch
    /// the safe surface (display name + phones) so a typical "fix a
    /// typo in the phone field" save can't surface a confusing 403.
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
    /// null when the input has no usable filter — Graph rejects an empty
    /// string. Search uses <c>startswith</c> on the most-common columns;
    /// it's the cheapest predicate Entra indexes.
    /// </summary>
    public static string? BuildGraphFilter(UserFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var clauses = new List<string>(3);

        if (filter.IsEnabled is { } enabled)
        {
            // Entra's "lockout" mirrors VisuAuth's "isEnabled" — flipped.
            // IsLockedOut = true → accountEnabled = false.
            clauses.Add($"accountEnabled eq {enabled.ToString().ToLowerInvariant()}");
        }
        if (filter.IsLockedOut is { } lockedOut)
        {
            clauses.Add($"accountEnabled eq {(!lockedOut).ToString().ToLowerInvariant()}");
        }
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var needle = EscapeForFilter(filter.SearchTerm.Trim());
            clauses.Add(
                $"(startswith(displayName,'{needle}') " +
                $"or startswith(userPrincipalName,'{needle}') " +
                $"or startswith(mail,'{needle}'))");
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    private static string? FirstPhone(List<string>? phones)
        => phones is { Count: > 0 } ? phones[0] : null;

    /// <summary>
    /// OData $filter literals escape a single quote by doubling it. Search
    /// terms come from end-user input so this matters.
    /// </summary>
    private static string EscapeForFilter(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
