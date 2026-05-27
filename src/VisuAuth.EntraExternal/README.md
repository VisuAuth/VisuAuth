# VisuAuth.EntraExternal

Microsoft Entra **External ID** (formerly Azure AD B2C) adapter for VisuAuth. Targets customer-facing tenants where end users sign up and sign in with email + password local accounts (and optionally federated social providers) managed by Microsoft.

The admin surface VisuAuth exposes is the same `/visuauth/admin/*` pages the Workforce adapter and the Identity adapter feed. Capability flags swap the experience automatically:

- **Login** — the email + password form on `/visuauth/login` disappears in favour of a "Sign in with Microsoft" hint, because `SupportsLocalLogin = false`. The actual hosted login lives at `{tenant}.ciamlogin.com` and is wired in the consumer's app via `Microsoft.Identity.Web` (see [v0.3 PR-C](https://github.com/VisuAuth/visuauth/issues)).
- **Admin** — `/visuauth/admin/users` lists customers live from Microsoft Graph. Create / disable / reset password / revoke sessions all hit Graph endpoints under the consumer's app-only credentials.
- **Roles** — `/visuauth/admin/roles` surfaces the app roles declared in your registered app's manifest. Assign / remove via `/users/{id}/appRoleAssignments`.

Built against [`Microsoft.Graph`](https://www.nuget.org/packages/Microsoft.Graph) 5.x with [`Azure.Identity`](https://www.nuget.org/packages/Azure.Identity) `ClientSecretCredential` for the app-only flow. Shares the underlying Graph wiring with [`VisuAuth.Entra`](https://www.nuget.org/packages/VisuAuth.Entra) via the [`VisuAuth.EntraCore`](https://www.nuget.org/packages/VisuAuth.EntraCore) package.

> **When to pick this adapter over `VisuAuth.Entra`.** Workforce = employees of *your* company (Entra ID). External = customers of your SaaS who sign up themselves (Entra External ID / customer identity). If you'd be issuing invite links via email to colleagues, you want Workforce. If users land on a signup page and self-onboard, you want External.

---

## Table of contents

1. [Install](#install)
2. [What this adapter does and doesn't do (capabilities)](#what-this-adapter-does-and-doesnt-do-capabilities)
3. [Set up an Entra External tenant + app registration](#set-up-an-entra-external-tenant--app-registration)
4. [Configure VisuAuth.EntraExternal in your app](#configure-visuauthentraexternal-in-your-app)
5. [Operations supported](#operations-supported)
6. [Known limitations](#known-limitations)
7. [Troubleshooting](#troubleshooting)
8. [Samples](#samples)

---

## Install

```sh
dotnet add package VisuAuth
dotnet add package VisuAuth.EntraExternal
```

`VisuAuth` is the meta-package (Admin UI + End-user UI + Abstractions). `VisuAuth.EntraExternal` brings Microsoft.Graph + Azure.Identity through the shared `VisuAuth.EntraCore`. Identity- or Workforce-adapter consumers don't pay this weight unless they reference `VisuAuth.EntraExternal`.

Minimal `Program.cs` for an External-only app (the one in `samples/Sample.EntraExternalWebApp` is ~30 lines):

```csharp
using VisuAuth;
using VisuAuth.EntraExternal.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddVisuAuth()
    .AddAdminUi()
    .AddEndUserUi();

builder.Services.AddVisuAuthEntraExternal(builder.Configuration);

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapVisuAuth();
app.Run();
```

No `AddIdentity`, no `AddDbContext`, no JWT issuer, no OAuth providers. Everything Identity-shaped is opt-in via the other adapter.

---

## What this adapter does and doesn't do (capabilities)

`UserBackendCapabilities` drive every conditional render in the UI. The Entra External adapter declares:

| Capability | Value | Why |
|---|---|---|
| `SupportsLocalLogin` | `false` | Microsoft owns the hosted customer login UX at `{tenant}.ciamlogin.com`. The `/visuauth/login` page hides the email/password form on its own. |
| `SupportsRegistration` | `true` | Admin Create at `/admin/users/new` works through Graph `POST /users` with an `identities[]` array. End-user self-service signup is hosted by Microsoft and wired via `Microsoft.Identity.Web` in v0.3 PR-C. |
| `SupportsPasswordReset` | `true` | Admin reset issues a temporary password via Graph `PATCH /users/{id}` (passwordProfile). End-user SSPR lives on the hosted Microsoft surface. |
| `SupportsTwoFactor` | `false` | Multi-factor enrolment happens on Microsoft's hosted surfaces. VisuAuth's TOTP pages don't apply. |
| `SupportsTwoFactorReset` | `false` *(v0.3 scope)* | Per-method DELETE in Graph needs typed builders per subtype. v0.4 covers it (shared with the Workforce adapter). |
| `SupportsLockout` | `false` | Entra has smart lockout that admins don't toggle per-user. Admin "lock" is `SetEnabled(false)` instead. |
| `SupportsEmailConfirmation` | `false` | Microsoft validates emails during the hosted signup flow. |
| `SupportsRoleManagement` | `true` | App roles via Graph. Create/Rename/Delete throw NotSupported (declared in the application manifest); List + Assign + Remove work. |
| `SupportsSessionRevocation` | `true` | Graph `revokeSignInSessions` invalidates every refresh token. |
| `SupportsExternalProviders` | `false` | Federated providers (Google, Facebook, Apple) ARE supported by External ID, but they're configured at the tenant level and rendered by the hosted Microsoft login page — not by VisuAuth's providers admin section. |
| `SupportsCustomClaims` | `true` | Graph extension properties + the user-attribute collection External ID ships with (v0.4 surfaces in the UI). |
| `SupportsAuditLog` | `true` | Entra has its own auditLogs API. The flag hides the in-tenant EF audit-log section (irrelevant for an External-only consumer). |
| `EmailDomainSuffix` | from `EntraExternalOptions.DefaultEmailDomain` | When set, locks the Create-User email input to that suffix. |

Every flag above is a real decision point in the UI — flipping one reshapes what the admin sees.

---

## Set up an Entra External tenant + app registration

Walkthrough that takes a fresh Microsoft account to a working VisuAuth.EntraExternal in ~25 minutes. Entra External tenants are free to create (unlike Workforce after the 2024 policy change), making this the lowest-friction path to try VisuAuth against a real Graph backend.

### Step 1 — Create the External tenant

1. Browse to [`entra.microsoft.com`](https://entra.microsoft.com), sign in.
2. **Manage tenants** (in the home page Shortcuts strip) → **+ Create**.
3. Pick **External** → **Continue**.
4. Fill the form (`Tenant name`, `Initial domain name`, `Location`) and **Review + create**. The initial domain becomes `{name}.onmicrosoft.com` — that's your **`TenantDomain`** value (write it down).
5. After ~30 seconds, **Go to tenant** (or switch via the top-right account menu).
6. From the tenant **Overview** card, copy the **Tenant ID** (GUID) — that's your `TenantId` config value.

### Step 2 — Register the management app

1. Sidebar **Identity** → **Applications** → **App registrations** → **+ New registration**.
2. Name (e.g. `VisuAuth Admin`), **Accounts in this organizational directory only**, leave **Redirect URI** blank for now (PR-C will add an OIDC redirect for the customer-facing app — this management app doesn't need one).
3. After registering, copy from the Overview blade:
   - **Application (client) ID** → goes into `VisuAuth:EntraExternal:ClientId`.

### Step 3 — Microsoft Graph permissions

1. App's sidebar → **API permissions** → **+ Add a permission** → **Microsoft Graph** → **Application permissions** (NOT Delegated).
2. Add all four:
   - `User.Read.All`
   - `User.ReadWrite.All`
   - `AppRoleAssignment.ReadWrite.All`
   - `Application.Read.All`
3. **Grant admin consent for {your tenant}** at the top of the permissions table. Each row should show a green ✓ "Granted" check.

> Forgetting "Grant admin consent" is the most common setup mistake — without it Graph returns `Authorization_RequestDenied` on every call.

### Step 4 — Client secret

1. App's sidebar → **Certificates & secrets** → **+ New client secret**.
2. Description (any), Expires 180 or 365 days for dev. **Add**.
3. **Immediately copy the Value column** (not Secret ID) — it's only shown once.

### Step 5 — (Optional) Declare app roles

Skip if you don't need the `/admin/roles` page populated. Otherwise:

1. App's sidebar → **App roles** → **+ Create app role**.
2. Display name `Admin`, value `admin`, **Allowed member types: Users/Groups**, **Enabled**.
3. Repeat for `Editor` or whatever roles your app needs.

### Step 6 — Populate user-secrets (dev) or environment (prod)

Dev:
```powershell
cd path/to/your/app
dotnet user-secrets set "VisuAuth:EntraExternal:TenantId"     "<directory-guid>"
dotnet user-secrets set "VisuAuth:EntraExternal:ClientId"     "<application-guid>"
dotnet user-secrets set "VisuAuth:EntraExternal:ClientSecret" "<value-from-step-4>"
dotnet user-secrets set "VisuAuth:EntraExternal:TenantDomain" "<initial-domain-from-step-1>"
```

Production / containerised — use env vars with double-underscore (ASP.NET Core convention):
```
VisuAuth__EntraExternal__TenantId
VisuAuth__EntraExternal__ClientId
VisuAuth__EntraExternal__ClientSecret
VisuAuth__EntraExternal__TenantDomain
```

That's it — `dotnet run`, browse `/visuauth/admin/users`, and you'll see the tenant's directory.

---

## Configure VisuAuth.EntraExternal in your app

`EntraExternalOptions` (everything bound from `VisuAuth:EntraExternal`):

| Property | Required | Default | Notes |
|---|---|---|---|
| `TenantId` | ✅ | — | Directory GUID. Validate-on-start fails fast if missing. |
| `ClientId` | ✅ | — | Application (client) ID. |
| `ClientSecret` | ✅ | — | App secret VALUE (not Secret ID). |
| `TenantDomain` | ✅ | — | Initial domain `{tenant}.onmicrosoft.com`. Used as the `issuer` when minting an External user's `identities[]` entry. |
| `AppRoleResourceId` | ❌ | falls back to `ClientId` | **Application ID** (not object ID!) of the app whose `appRoles` the role store surfaces. |
| `GraphBaseUrl` | ❌ | `https://graph.microsoft.com/v1.0` | Override for sovereign clouds if Microsoft ships External there. |
| `DefaultEmailDomain` | ❌ | null | Email domain to suggest in the admin Create User form. Unlike Workforce, External is permissive — customers can use any email domain. |

Two `AddVisuAuthEntraExternal` overloads — pick whichever fits the wiring style:

```csharp
// Lambda — best when values come from code / env vars
builder.Services.AddVisuAuthEntraExternal(o =>
{
    o.TenantId = builder.Configuration["VisuAuth:EntraExternal:TenantId"]!;
    o.ClientId = builder.Configuration["VisuAuth:EntraExternal:ClientId"]!;
    o.ClientSecret = builder.Configuration["VisuAuth:EntraExternal:ClientSecret"]!;
    o.TenantDomain = builder.Configuration["VisuAuth:EntraExternal:TenantDomain"]!;
});

// Configuration section — best with appsettings / user-secrets / Key Vault
builder.Services.AddVisuAuthEntraExternal(builder.Configuration);
```

Both register the same service graph (TryAdd, so a consumer-registered test double wins): `IUserStore` → `EntraExternalUserStore`, `IRoleStore` → `EntraExternalRoleStore`, `IAuthenticationFlow` → `EntraExternalAuthenticationFlow`, plus stub `IAuditWriter` / `IJwtIssuer` / `ITenantContext` / `IExternalLoginFlow` so the End-user UI can resolve without Identity wired alongside.

---

## Operations supported

### `IUserStore` (admin user management)

| Method | Status | Graph endpoint(s) |
|---|---|---|
| `ListAsync` | ✅ | `GET /users` with `identities` in the select; the search clause matches against `identities/issuerAssignedId` so the customer-typed email is searchable |
| `GetAsync` / `GetDetailAsync` | ✅ | `GET /users/{id}` + role resolution against `/servicePrincipals` |
| `CreateAsync` | ✅ | `POST /users` with `identities[].signInType = emailAddress` and `issuer = TenantDomain` |
| `UpdateAsync` | ✅ partial | `PATCH /users/{id}` — DisplayName + BusinessPhones only. Identities / UPN / mail are deliberately not patched (rewriting the customer's identity from the admin UI would lock them out). |
| `DeleteAsync` | ✅ | `DELETE /users/{id}` |
| `SetEnabledAsync` | ✅ | `PATCH /users/{id}` (`accountEnabled`) |
| `ResetPasswordAsync` | ✅ | `PATCH /users/{id}` (`passwordProfile` with `forceChangePasswordNextSignIn = true`) |
| `RevokeSessionsAsync` | ✅ | `POST /users/{id}/revokeSignInSessions` |
| `ResetTwoFactorAsync` | ❌ throws NotSupported (v0.4) | Per-method DELETE needs typed builders per auth-method subtype |

### `IRoleStore` (app role management)

Identical surface to the Workforce adapter — app roles are tenant-family-agnostic in Graph. See the [Workforce adapter README](https://github.com/VisuAuth/visuauth/blob/main/src/VisuAuth.Entra/README.md#irolestore-app-role-management) for the per-method table.

### `IAuthenticationFlow` (end-user surface — capability-driven)

Every method returns either `RedirectToExternalProvider` (`SignInWithPasswordAsync`) or a `UserResult.Failure` with the "use Microsoft" message. The Login page hides the password form on its own via `SupportsLocalLogin = false`; this flow exists as a safety net for direct-API / CLI callers. The real customer-facing OIDC redirect lands in v0.3 PR-C via `Microsoft.Identity.Web`.

---

## Known limitations

| What | Why | Workaround |
|---|---|---|
| Identities / UPN / mail not editable from the admin UI | Rewriting a customer's identity from a generic admin form would lock them out of their own account | A dedicated email-change flow with verification mail is on the v0.4 roadmap. For now, edit identities in the Entra portal |
| `signInActivity` is not in the default user `$select` | Field needs `AuditLog.Read.All` + Entra ID P1 license; free External tenants get a 403 on the whole list call | `UserSummary.LastSignInAt` stays null; UI renders "—". Subclass / override the store if you're on a paid tier |
| Pagination treats every list call as page 1 | Graph paginates with `@odata.nextLink` cursors, not numeric pages; the v0.3 `PagedResult.Page` contract is 1-based | Use search / filter to refine. v0.4 adds cursor-based paging shared with the Workforce adapter |
| `ResetTwoFactorAsync` throws | Per-method DELETE needs typed builders per auth-method subtype | Use the Entra portal's Authentication methods blade for the user |
| End-user `/visuauth/login` shows "use external provider" with no provider button | v0.3 PR-B doesn't ship the OIDC wiring | v0.3 PR-C adds `Microsoft.Identity.Web` integration and a real "Sign in with Microsoft" button |
| Federated identities (Google, Facebook, Apple) not surfaced in the admin UI | The `identities[]` array can hold multiple entries; v0.3 detail page renders only the local-account one | Configure providers in the Entra portal's "Identity providers" blade; v0.4+ may render the full identities list on the detail page |

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `OptionsValidationException: The TenantDomain field is required` (startup) | Missing user-secret / env var | Set `VisuAuth:EntraExternal:TenantDomain` to the initial domain `{tenant}.onmicrosoft.com` |
| `Authorization_RequestDenied: Insufficient privileges` | App permissions weren't granted admin consent, or wrong type (Delegated instead of Application) | Re-check API permissions: must be **Application**, must show "Granted for {tenant}" |
| `One or more properties contains invalid values` on Create | Identities array missing or the `issuer` doesn't match the configured `TenantDomain` | Confirm `TenantDomain` matches the tenant's actual initial domain (no `https://`, no trailing slash) |
| `A null value was found for the property named 'businessPhones'` | Sending null where Graph expects an array | The mapper sends `[]` for "no phone" — file a bug if you still hit this |

---

## Samples

### `samples/Sample.EntraExternalWebApp` — minimal External-only reference

A ~30-line `Program.cs` showing the shortest path to a working VisuAuth admin against Microsoft Graph for an External tenant. No Identity / SQLite / JWT / OAuth wire-up. Mirrors `samples/Sample.EntraWebApp` (the Workforce equivalent) for easy comparison.

```powershell
cd samples/Sample.EntraExternalWebApp
dotnet run
```

Lands on `http://localhost:5260/visuauth/admin` (the launch profile opens the browser there directly).
