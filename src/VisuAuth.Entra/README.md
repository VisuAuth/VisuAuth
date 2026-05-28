# VisuAuth.Entra

Microsoft Entra ID (Workforce) adapter for VisuAuth. Plugs into the same admin UI and end-user pages the rest of VisuAuth ships — capability flags swap the experience automatically:

- **Login** — the email + password form on `/visuauth/login` disappears and the page becomes a "Sign in with Microsoft" hint, because `SupportsLocalLogin = false`.
- **Admin** — `/visuauth/admin/users` lists the directory live from Microsoft Graph. Create / update / delete / reset password / revoke sessions all hit Graph endpoints under the consumer's app-only credentials.
- **Roles** — `/visuauth/admin/roles` surfaces the app roles declared in your registered app's manifest. Assign / remove via `/users/{id}/appRoleAssignments`.

Built against [`Microsoft.Graph`](https://www.nuget.org/packages/Microsoft.Graph) 5.x with [`Azure.Identity`](https://www.nuget.org/packages/Azure.Identity) `ClientSecretCredential` for the app-only flow.

> **Scope.** v0.2 covers **Entra ID Workforce** tenants. Entra **External ID** (the B2C successor) is on the v0.3 roadmap (`VisuAuth.EntraExternal`) — admin CRUD shares the Graph endpoints, but the signup / signin flows and the user-shape (identities, user attributes) differ enough to deserve a dedicated adapter.

---

## Table of contents

1. [Install](#install)
2. [What this adapter does and doesn't do (capabilities)](#what-this-adapter-does-and-doesnt-do-capabilities)
3. [Set up an Entra tenant + app registration](#set-up-an-entra-tenant--app-registration)
4. [Configure VisuAuth.Entra in your app](#configure-visuauthentra-in-your-app)
5. [Multi-domain (verified custom domain)](#multi-domain-verified-custom-domain)
6. [Operations supported](#operations-supported)
7. [Known limitations](#known-limitations)
8. [Troubleshooting](#troubleshooting)
9. [Samples](#samples)

---

## Install

```sh
dotnet add package VisuAuth
dotnet add package VisuAuth.Entra
```

`VisuAuth` is the meta-package (Admin UI + End-user UI + Abstractions). `VisuAuth.Entra` brings Microsoft.Graph + Azure.Identity. Identity-adapter consumers don't pay this weight unless they reference `VisuAuth.Entra`.

Minimal `Program.cs` for an Entra-only app (the one in `samples/Sample.EntraWebApp` is ~30 lines):

```csharp
using VisuAuth;
using VisuAuth.Entra.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddVisuAuth()
    .AddAdminUi()
    .AddEndUserUi();

builder.Services.AddVisuAuthEntra(builder.Configuration);

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

`UserBackendCapabilities` drive every conditional render in the UI. The Entra adapter declares:

| Capability | Value | Why |
|---|---|---|
| `SupportsLocalLogin` | `false` | Microsoft owns the login UX. The `/visuauth/login` page hides the email/password form on its own. |
| `SupportsRegistration` | `true` | Admin Create at `/admin/users/new` works through Graph `POST /users`. End-user self-service signup (`/visuauth/register`) is gated by Microsoft and resolves to a graceful failure. |
| `SupportsPasswordReset` | `true` | Admin reset issues a temporary password via Graph `PATCH /users/{id}` (passwordProfile). End-user SSPR lives in the Entra portal. |
| `SupportsTwoFactor` | `false` | Microsoft Authenticator is set up via Microsoft's own surfaces. VisuAuth's TOTP pages don't apply. |
| `SupportsTwoFactorReset` | `true` | Admin "reset 2FA" deletes the user's registered authentication methods via Graph (per-subtype DELETE). Needs `UserAuthenticationMethod.ReadWrite.All`. |
| `SupportsLockout` | `false` | Entra has smart lockout that admins don't toggle per-user. Admin "lock" is `SetEnabled(false)` instead. |
| `SupportsEmailConfirmation` | `false` | Entra validates emails on user creation. |
| `SupportsRoleManagement` | `true` | App roles via Graph. Create/Rename/Delete throw NotSupported (declared in the application manifest, not at runtime); List + Assign + Remove work. |
| `SupportsSessionRevocation` | `true` | Graph `revokeSignInSessions` invalidates every refresh token. |
| `SupportsExternalProviders` | `false` | Entra IS the IdP — the providers admin page would be circular. |
| `SupportsCustomClaims` | `true` | Graph extension properties (v0.3 surfaces them in the UI). |
| `SupportsAuditLog` | `true` | Entra has its own auditLogs API (v0.3 wires the reader). |
| `EmailDomainSuffix` | from `EntraOptions.DefaultEmailDomain` | When set, locks the Create-User email input to that suffix. Bypasses Graph's "domain must be verified" 400. |

Every flag above is a real decision point in the UI — flipping one reshapes what the admin sees.

---

## Set up an Entra tenant + app registration

Walkthrough that takes a fresh Microsoft account to a working VisuAuth.Entra in ~20 minutes.

### Step 1 — Tenant (skip if you already have one)

> **Heads-up — Microsoft policy change (2024).** Creating a *new* **Workforce** tenant now requires a paid license (M365 Business / Entra ID P1+). Tenants created before this change are grandfathered. Free options today: Entra **External** (different feature set — see "Scope" above) and your work-or-school account's existing tenant.

1. Browse to [`entra.microsoft.com`](https://entra.microsoft.com), sign in.
2. **Manage tenants** (in the home page Shortcuts strip) → **+ Create**.
3. Pick **Workforce** → **Continue**.
4. Fill the form (`Organization name`, `Initial domain name`, `Country`) and **Create**.
5. Switch into the new tenant when prompted; copy the **Tenant ID** (GUID) from the home card.

### Step 2 — App registration

1. Sidebar **Entra ID** → **App registrations** → **+ New registration**.
2. Name (e.g. `VisuAuth Sample`), **Accounts in this organizational directory only**, leave **Redirect URI** blank.
3. After registering, copy from the Overview blade:
   - **Application (client) ID** → goes into `VisuAuth:Entra:ClientId`.
   - **Directory (tenant) ID** → confirms the value from Step 1.

### Step 3 — Microsoft Graph permissions

1. App's sidebar → **API permissions** → **+ Add a permission** → **Microsoft Graph** → **Application permissions** (NOT Delegated).
2. Add all four:
   - `User.Read.All`
   - `User.ReadWrite.All`
   - `AppRoleAssignment.ReadWrite.All`
   - `Application.Read.All`
3. **Grant admin consent for {your tenant}** at the top of the permissions table. Each row should show a green ✓ "Granted" check.

> Forgetting "Grant admin consent" is the single most common setup mistake — without it Graph returns `Authorization_RequestDenied` on every call.

### Step 4 — Client secret

1. App's sidebar → **Certificates & secrets** → **+ New client secret**.
2. Description (any), Expires 180 or 365 days for dev. **Add**.
3. **Immediately copy the Value column** (not Secret ID) — it's only shown once, after that it stays masked. Lose it = delete and create a new one.

### Step 5 — (Optional) Declare app roles

Skip if you don't need the `/admin/roles` page populated. Otherwise:

1. App's sidebar → **App roles** → **+ Create app role**.
2. Display name `Admin`, value `admin`, **Allowed member types: Users/Groups**, **Enabled**.
3. Repeat for `Editor` or whatever roles your app needs.

App roles are declared statically in the app manifest — VisuAuth.Entra surfaces them read-only on `/admin/roles` and assigns/removes them via Graph at runtime, but Create/Rename/Delete have to happen in the portal.

### Step 6 — Populate user-secrets (dev) or environment (prod)

Dev:
```powershell
cd path/to/your/app
dotnet user-secrets set "VisuAuth:Entra:TenantId"     "<directory-guid>"
dotnet user-secrets set "VisuAuth:Entra:ClientId"     "<application-guid>"
dotnet user-secrets set "VisuAuth:Entra:ClientSecret" "<value-from-step-4>"
```

Production / containerised — use env vars with double-underscore (ASP.NET Core convention):
```
VisuAuth__Entra__TenantId
VisuAuth__Entra__ClientId
VisuAuth__Entra__ClientSecret
```

Or any other `IConfiguration` source (Key Vault, AWS Secrets Manager, etc.) under the section name `VisuAuth:Entra`.

That's it — `dotnet run`, browse `/visuauth/admin/users`, and you'll see the tenant's directory.

---

## Configure VisuAuth.Entra in your app

`EntraOptions` (everything bound from `VisuAuth:Entra`):

| Property | Required | Default | Notes |
|---|---|---|---|
| `TenantId` | ✅ | — | Directory GUID. Validate-on-start fails fast if missing. |
| `ClientId` | ✅ | — | Application (client) ID. |
| `ClientSecret` | ✅ | — | App secret VALUE (not Secret ID). |
| `AppRoleResourceId` | ❌ | falls back to `ClientId` | **Application ID** (not object ID!) of the app whose `appRoles` the role store surfaces. Defaults to "manage VisuAuth's own roles". |
| `GraphBaseUrl` | ❌ | `https://graph.microsoft.com/v1.0` | Override for sovereign clouds (Entra Government, China). |
| `DefaultEmailDomain` | ❌ | null | Verified domain to suggest in the admin Create User form. See [Multi-domain](#multi-domain-verified-custom-domain). |

Two `AddVisuAuthEntra` overloads — pick whichever fits the wiring style:

```csharp
// Lambda — best when values come from code / env vars
builder.Services.AddVisuAuthEntra(o =>
{
    o.TenantId = builder.Configuration["VisuAuth:Entra:TenantId"]!;
    o.ClientId = builder.Configuration["VisuAuth:Entra:ClientId"]!;
    o.ClientSecret = builder.Configuration["VisuAuth:Entra:ClientSecret"]!;
});

// Configuration section — best with appsettings / user-secrets / Key Vault
builder.Services.AddVisuAuthEntra(builder.Configuration);
```

Both register the same service graph (TryAdd, so a consumer-registered test double wins): `IUserStore` → `EntraUserStore`, `IRoleStore` → `EntraRoleStore`, `IAuthenticationFlow` → `EntraAuthenticationFlow`, plus stub `IAuditWriter` / `IJwtIssuer` / `ITenantContext` / `IExternalLoginFlow` so the End-user UI can resolve without Identity wired alongside.

---

## Multi-domain (verified custom domain)

Workforce tenants ship with one default domain (`{tenant}.onmicrosoft.com`). You can verify additional custom domains and create users under any of them. The Create-User UX picks one as the "default suggestion" via `DefaultEmailDomain`.

### Add a custom domain

1. In the target tenant: **Identity → Settings → Domain names → + Add custom domain**.
2. Type the domain (e.g. `acme.com`) → **Add domain**.
3. Entra shows a TXT record. Copy the `MS=ms…` value.
4. In your DNS provider, add a TXT record:
   - **Cloudflare:** DNS → Records → + Add record → Type `TXT`, Name `@`, Content `MS=...`, Proxy DNS-only. Propagates in seconds.
   - **Route53 / GoDaddy / Registro.br / others:** add a TXT record at the apex (`@`) with the `MS=...` value. Propagation is usually 5-30 minutes.
5. Verify propagation: `nslookup -type=TXT acme.com 1.1.1.1` should echo the `MS=...` value.
6. Back in Entra: **Verify**. Status flips to ✅ Verified.

> **Don't mark the custom domain as Primary** unless you have a reason — keeping the `*.onmicrosoft.com` slug as Primary avoids subtle Graph URL changes.

### Tell VisuAuth which domain to suggest

```powershell
dotnet user-secrets set "VisuAuth:Entra:DefaultEmailDomain" "acme.com"
```

After restart, `/admin/users/new` renders a split input: free text for the local part, `@acme.com` locked as a non-editable suffix. The PageModel concatenates before calling `CreateAsync`.

To create users in a non-default verified domain, pass the full email through the API (`CreateUserCommand.Email = "cto@subsidiary.com"`); the PageModel skips the suffix concatenation when the value already contains `@`.

To go back to a free-text email input, remove the option (`dotnet user-secrets remove "VisuAuth:Entra:DefaultEmailDomain"`).

---

## Operations supported

### `IUserStore` (admin user management)

| Method | Status | Graph endpoint(s) |
|---|---|---|
| `ListAsync` | ✅ | `GET /users` (with `$filter`, `$top`, `$select`, `ConsistencyLevel: eventual` for advanced filters) |
| `GetAsync` / `GetDetailAsync` | ✅ | `GET /users/{id}` + (`GET /users/{id}/appRoleAssignments` and `GET /servicePrincipals?$filter=appId` for the detail's roles list) |
| `CreateAsync` | ✅ | `POST /users` |
| `UpdateAsync` | ✅ partial | `PATCH /users/{id}` — DisplayName + BusinessPhones only. UPN / mail are deliberately not patched (Graph rejects for B2B guests). Edit those in the portal. |
| `DeleteAsync` | ✅ | `DELETE /users/{id}` |
| `SetEnabledAsync` | ✅ | `PATCH /users/{id}` (`accountEnabled`) |
| `ResetPasswordAsync` | ✅ | `PATCH /users/{id}` (`passwordProfile` with `forceChangePasswordNextSignIn = true`) |
| `RevokeSessionsAsync` | ✅ | `POST /users/{id}/revokeSignInSessions` |
| `ResetTwoFactorAsync` | ✅ | Lists `GET /users/{id}/authentication/methods` and deletes each removable method via its typed endpoint (`microsoftAuthenticatorMethods` / `fido2Methods` / `phoneMethods` / `softwareOathMethods` / `windowsHelloForBusinessMethods` / `emailMethods`). Password is left in place. Needs `UserAuthenticationMethod.ReadWrite.All`. |

### `IRoleStore` (app role management)

| Method | Status | Notes |
|---|---|---|
| `ListAsync` / `GetAsync` | ✅ | Read app roles from the target app's `servicePrincipal.appRoles`. |
| `GetRolesForUserAsync` | ✅ | Cross-reference `/users/{id}/appRoleAssignments` with the app roles catalogue. |
| `AssignRoleAsync` | ✅ | `POST /users/{id}/appRoleAssignments`. Requires user id to be a GUID. |
| `RemoveRoleAsync` | ✅ idempotent | `DELETE /users/{id}/appRoleAssignments/{assignmentId}`; no-op when the user doesn't have the role. |
| `CreateAsync` / `RenameAsync` / `DeleteAsync` | ❌ throws NotSupported | App roles are declared in the app manifest. Edit in the Entra portal. |

### `IAuthenticationFlow` (end-user surface — capability-driven)

Every method returns either `RedirectToExternalProvider` (`SignInWithPasswordAsync`) or a `UserResult.Failure` with the "use Microsoft" message. The Login page hides the password form on its own via `SupportsLocalLogin = false`; this flow exists as a safety net for direct-API / CLI callers.

---

## Known limitations

| What | Why | Workaround |
|---|---|---|
| `UPN` / `mail` not editable from the admin UI | Graph rejects PATCH on these for B2B externals (`*#EXT#@*`) with 403, even with `User.ReadWrite.All` | Edit in the Entra portal, or POST `PATCH /users/{id}` from custom code that branches on member-vs-guest |
| `signInActivity` is not in the default user `$select` | Field requires `AuditLog.Read.All` + Entra ID P1 license; free tenants get a 403 on the whole list call | `UserSummary.LastSignInAt` stays null; UI renders "—". Subclass / override the store if you're on P1+ |
| Pagination treats every list call as page 1 | Graph paginates with `@odata.nextLink` cursors, not numeric pages; the v0.2 `PagedResult.Page` contract is 1-based | Use search / filter to refine. v0.3 adds cursor-based paging |
| Multi-domain UI picks ONE default | The HTML form binds a single fixed suffix | API can override (pass a full email in `CreateUserCommand.Email`). v0.x may add a dropdown of verified domains |
| End-user `/visuauth/login` shows "use external provider" with no provider button | VisuAuth.Entra doesn't implement OIDC — the consumer wires `Microsoft.Identity.Web` to host the actual login | Add `Microsoft.Identity.Web` and configure a sign-in route (`/signin-microsoft` etc.). The VisuAuth login page is a hint, not the auth surface |

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `OptionsValidationException: The TenantId field is required` (startup) | Missing user-secret / env var, or running in Production without configuration | Set `VisuAuth:Entra:TenantId / ClientId / ClientSecret`. In Production, user-secrets are NOT loaded — use env vars / config |
| `Authorization_RequestDenied: Insufficient privileges` | App permissions weren't granted admin consent, or the wrong permission was added (Delegated instead of Application) | Re-check API permissions: must be **Application**, must show "Granted for {tenant}" |
| `The domain portion of the userPrincipalName property is invalid` on Create | Email uses a domain not verified on the tenant | Use `@{tenant}.onmicrosoft.com` or add + verify a custom domain. Configure `DefaultEmailDomain` so the UI guides the operator |
| `A null value was found for the property named 'businessPhones'` | Sending null where Graph expects an array | Already fixed in v0.2 — the mapper sends `[]` for "no phone" |
| `Insufficient privileges to complete the operation` on user update | Patching `userPrincipalName` / `mail` on a B2B guest | v0.2 mapper no longer patches those fields — only DisplayName + BusinessPhones come through |
| `The principal does not have required Microsoft Graph permission(s): AuditLog.Read.All` | `signInActivity` was in the `$select` | v0.2 mapper drops `signInActivity` from the default select. If you re-added it on a custom select, you need `AuditLog.Read.All` + P1 |
| Login page is blank / shows "use external provider" but no button | VisuAuth.Entra is not an OIDC server | Wire `Microsoft.Identity.Web` in your `Program.cs` for the actual user-facing sign-in |

---

## Samples

Two samples ship in the repo; both share the same `UserSecretsId` so dev secrets need to be set only once.

### `samples/Sample.WebApp` — dual-backend toggle

The "everything" sample. Defaults to the Identity adapter against local SQLite. Set `VISUAUTH_BACKEND=entra` (or `VisuAuth:Backend=entra` in config) to flip to the Entra adapter — same admin UI, different store underneath. Used to demonstrate the capability flag system end-to-end.

```powershell
cd samples/Sample.WebApp
$env:VISUAUTH_BACKEND="entra"
dotnet run
```

### `samples/Sample.EntraWebApp` — minimal Entra-only reference

A ~30-line `Program.cs` showing the shortest path to a working VisuAuth admin against Microsoft Graph. No Identity / SQLite / JWT / OAuth wire-up. Good template for an app that only ever uses Entra.

```powershell
cd samples/Sample.EntraWebApp
dotnet run
```

Lands on http://localhost:5240/visuauth/admin (the launch profile opens the browser there directly).
