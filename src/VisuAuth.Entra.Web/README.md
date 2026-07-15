# VisuAuth.Entra.Web

Operator sign-in for the [`VisuAuth.Entra`](https://www.nuget.org/packages/VisuAuth.Entra)
(Microsoft Entra ID / Workforce) adapter.

## Why you need it

`AddVisuAuthEntra(...)` wires Microsoft Graph with **app-only** credentials.
That authenticates *the app* to Microsoft — it does not sign *a human* in, and
it registers no authentication scheme at all.

The VisuAuth admin dashboard requires an authenticated user by default, so
without a sign-in scheme it has nothing to challenge with and the operator has
no way in. This package adds that scheme, wrapping `Microsoft.Identity.Web`:

```csharp
builder.Services.AddVisuAuth().AddAdminUi();
builder.Services.AddVisuAuthEntra(builder.Configuration);
builder.Services.AddVisuAuthEntraSignIn(builder.Configuration);

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapVisuAuth();
app.Run();
```

> Skip this package only if you already front `/visuauth/admin` with your own
> authentication, or you called `AllowAnonymousVisuAuthAdmin()` because the
> dashboard is fenced off some other way. **Never leave the Entra admin
> anonymous on a reachable network** — the adapter holds directory-wide Graph
> permissions, so an anonymous visitor would be administering your real tenant.

## Configuration

Bind from `VisuAuth:Entra:Web` (a **separate app registration** from the Graph
one — see below):

```bash
dotnet user-secrets set "VisuAuth:Entra:Web:TenantId"     "<guid>"
dotnet user-secrets set "VisuAuth:Entra:Web:ClientId"     "<guid>"
dotnet user-secrets set "VisuAuth:Entra:Web:ClientSecret" "<value>"
```

| Key | Default | Notes |
|---|---|---|
| `TenantId` | *(required)* | Usually the same GUID as `VisuAuth:Entra:TenantId`. |
| `ClientId` | *(required)* | The **sign-in** app registration, not the Graph app. |
| `ClientSecret` | — | Required for a confidential web client. |
| `Instance` | `https://login.microsoftonline.com/` | Override for sovereign clouds. |
| `CallbackPath` | `/signin-oidc` | Must match the registration's redirect URI exactly. |
| `SignedOutCallbackPath` | `/signout-callback-oidc` | Must match the post-logout redirect URI. |

### App registration

Add a redirect URI of `https://<your-host>/signin-oidc` (Web platform) and a
post-logout redirect URI of `https://<your-host>/signout-callback-oidc`. No
Graph application permissions are needed here — this registration only signs
operators in; the Graph app is the one with `User.ReadWrite.All` and friends.

### Why two app registrations?

The Graph adapter uses app-only client credentials (the app acts as itself);
sign-in uses the authorization-code flow (a human acts as themselves). Microsoft
treats these as separate apps, and sharing one means the same secret signs both
your admin Graph calls and your sign-in redirects.

## Restricting to a role

By default any authenticated user in the tenant reaches the dashboard. To limit
it to an app role, register a policy under VisuAuth's admin policy name:

```csharp
using VisuAuth.AdminUi.DependencyInjection;

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy,
        policy => policy.RequireRole("VisuAuth.Admin"));
```

Declare the app role on the sign-in registration's manifest and assign it to the
operators who should get in.

## License

Apache 2.0 — see the [repository](https://github.com/VisuAuth/VisuAuth).
