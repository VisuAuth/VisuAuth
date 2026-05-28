# VisuAuth.EntraExternal.Web

End-user OIDC sign-in for the VisuAuth Entra External adapter. Opt-in sub-package: wraps `Microsoft.Identity.Web` so the consumer's `/visuauth/login` page renders a working "Sign in with Microsoft" button that round-trips through the External tenant's hosted page at `{tenant}.ciamlogin.com`.

> **Why a separate package.** Microsoft.Identity.Web is a heavy dependency (cookies, OpenIdConnect, token caching, MSAL). Consumers who only want admin CRUD against the directory (via `VisuAuth.EntraExternal`) don't pay that weight. Add this package only when you want the End-user UI to actually authenticate customers.

---

## Install

```sh
dotnet add package VisuAuth
dotnet add package VisuAuth.EntraExternal
dotnet add package VisuAuth.EntraExternal.Web
```

Minimal `Program.cs`:

```csharp
using VisuAuth;
using VisuAuth.EntraExternal.DependencyInjection;
using VisuAuth.EntraExternal.Web.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddVisuAuth()
    .AddAdminUi()
    .AddEndUserUi();

builder.Services.AddVisuAuthEntraExternal(builder.Configuration);
builder.Services.AddVisuAuthEntraExternalSignIn(builder.Configuration);

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapVisuAuth();
app.Run();
```

The sign-in section binds from `VisuAuth:EntraExternal:Web` by default — distinct from the admin Graph section (`VisuAuth:EntraExternal`) because **sign-in and Graph use different app registrations** in a typical deployment.

---

## Configure

| Property | Required | Default | Notes |
|---|---|---|---|
| `TenantSubdomain` | ✅ | — | The `{tenant}` in `{tenant}.ciamlogin.com`. Used to build the OIDC authority URL. |
| `TenantId` | ✅ | — | Directory (tenant) GUID. Typically the same GUID as `VisuAuth:EntraExternal:TenantId`. |
| `ClientId` | ✅ | — | Application (client) ID of the **end-user-facing** app registration (the one with the OIDC redirect URI). NOT the admin Graph app. |
| `ClientSecret` | ❌ * | null | Client secret for the end-user app registration. Required for confidential web clients; optional if the app is registered as public. |
| `CallbackPath` | ❌ | `/signin-oidc` | OIDC callback path. Must match the redirect URI configured on the app registration EXACTLY. |
| `SignedOutCallbackPath` | ❌ | `/signout-callback-oidc` | Post-logout redirect path. |
| `SignInUserFlow` | ❌ | null | Hint for the sign-in user flow name; the hosted page uses the tenant's default binding when null. |
| `ProfileSync:Enabled` | ❌ | `false` | Opt-in: copy id_token claims onto the Graph user on sign-in (see below). |
| `ProfileSync:ClaimToGraphProperty` | ❌ | `given_name`→`givenName`, `family_name`→`surname` | Claim-type → Graph-property map. Config entries are added to the defaults. |

### Profile attribute sync (claims → Graph user)

When your sign-up user flow collects attributes and emits them as token claims, VisuAuth can copy them onto the directory user on each sign-in — no Graph claims-mapping policy or beta API needed. It's **off by default**; opt in:

```jsonc
"VisuAuth": {
  "EntraExternal": {
    "Web": {
      "ProfileSync": {
        "Enabled": true,
        "ClaimToGraphProperty": {
          // given_name -> givenName and family_name -> surname are always included.
          // Add your custom-attribute claims here:
          "extension_<appId>_country": "country",
          "extension_<appId>_company": "companyName"
        }
      }
    }
  }
}
```

On a successful sign-in, every mapped claim present on the token is PATCHed onto the user via the standard v1.0 `PATCH /users/{id}`. Notes:

- **Supported target properties** (the map's values, case-insensitive): `givenName`, `surname`, `displayName`, `jobTitle`, `department`, `companyName`, `city`, `state`, `country`, `postalCode`, `streetAddress`. Unknown targets are skipped (logged), so a typo can't break sign-in.
- **Best-effort**: a Graph PATCH failure is logged and swallowed — it never blocks a sign-in the customer already completed.
- **Claims are the source of truth on each sign-in** while enabled: a mapped property is overwritten from the token whenever the claim is present. Properties with no corresponding claim are left untouched.
- Custom directory **extension** properties (schema-qualified) are out of scope here — they need the schema extension registered first. This path writes only the standard `User` properties listed above.

> **Why not a user-flow admin page?** Listing / editing user flows + their attributes lives in the **beta** Microsoft Graph API; this adapter stays on the stable v1.0 SDK. Manage the flows themselves in the Entra portal; VisuAuth maps the resulting claims.

\* Required unless the app registration is a public client.

---

## Two app registrations explained

Entra External deployments typically split admin and end-user flows across two separate registered apps in the same tenant:

| Concern | App registration | Auth flow | Credentials section |
|---|---|---|---|
| Admin reads/writes the directory through Graph | "VisuAuth Admin" | Client credentials (app-only) | `VisuAuth:EntraExternal` |
| Customer signs into the consumer's app via hosted page | "VisuAuth End-user" | Authorization code + PKCE (OIDC) | `VisuAuth:EntraExternal:Web` |

The admin app needs Graph `User.ReadWrite.All` + friends with admin consent. The end-user app needs a redirect URI configured for your domain (e.g. `https://app.example.com/signin-oidc`) and the OIDC scopes (`openid profile email`).

Sharing a single app between the two flows is technically possible but adds security surface (the same client secret signs both admin Graph requests and end-user redirects). The Microsoft documentation recommends keeping them separate.

---

## How sign-in works end-to-end

1. User visits `/visuauth/login` — VisuAuth's End-user page reads `IExternalLoginFlow.GetProvidersAsync()`. With this package wired, that returns one provider: `Sign in with Microsoft`.
2. User clicks the button. The form POSTs to `/visuauth/external-login/start` with `scheme=MicrosoftEntraExternal`.
3. `StartModel` issues `ChallengeAsync` against the registered OIDC handler. Microsoft.Identity.Web redirects the user to `{tenant}.ciamlogin.com/{tenant-id}/v2.0/authorize`.
4. User signs in on Microsoft's hosted page.
5. Microsoft posts back to `/signin-oidc`. Microsoft.Identity.Web validates the id_token, writes the Cookies scheme cookie, redirects to `/visuauth/external-login/callback`.
6. The Callback page calls `_externalLogin.CompleteSignInAsync(strategy)`. This package's implementation reads the authenticated principal's `oid` claim, verifies the user exists in Graph via `IUserStore.GetAsync`, and returns `Success` with the directory id.
7. Callback page redirects to `returnUrl` (or `/`).

The user is now signed in — `HttpContext.User.Identity.IsAuthenticated == true`, claims include their Graph object id, email, name.

---

## First-time strategy

VisuAuth's `ExternalLoginOptions.FirstTimeStrategy` (defaults to `AutoCreate`) controls what happens on the very first sign-in when the local store doesn't yet have the user. For Entra External:

| Strategy | What happens |
|---|---|
| `AutoCreate` (recommended) | User must already exist in the Entra directory (hosted signup flow created them). Sign-in proceeds. If the user is somehow missing, surfaces a clean "user not found in directory" failure. |
| `AutoLinkByEmailOrConfirm` | Returns a graceful failure with an actionable message. Confirmation pages can't create users in External — Microsoft owns directory creation through user flows. |
| `AlwaysConfirm` | Same as above. |

In practice, leave the default. v0.3 PR D adds the signup user-flow integration that lets the consumer point the "Sign in with Microsoft" button at a sign-up flow rather than a sign-in flow when they want self-service registration.

---

## Known limitations

| What | Why | Workaround |
|---|---|---|
| Only one user flow per app | The OIDC handler is configured against one authority + one default flow at startup | Run multiple apps, or pick the flow in the Entra portal's policy binding |
| No admin UI for listing / editing user flows | The user-flow + user-flow-attribute Graph APIs are beta-only; this adapter stays on the stable v1.0 SDK | Manage user flows in the Entra portal. VisuAuth maps the resulting token claims onto the Graph user (see [Profile attribute sync](#profile-attribute-sync-claims--graph-user)) |
| Profile sync writes only standard `User` properties | Custom directory extension properties need a registered schema extension + their qualified names | Map collected attributes to the closest standard property (e.g. a "company" attribute → `companyName`) |

---

## Sample

`samples/Sample.EntraExternalWebApp` wires this package — see its `Program.cs` for end-to-end setup including the `VisuAuth:EntraExternal:Web` configuration section in `appsettings.json`.
