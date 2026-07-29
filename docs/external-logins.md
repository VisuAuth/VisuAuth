# External login providers

"Sign in with Google / Microsoft / GitHub / Apple" on the end-user pages, plus an
admin screen to manage the credentials without a redeploy.

VisuAuth doesn't implement OAuth itself — you register the providers with ASP.NET
Core's standard `AddGoogle()` / `AddMicrosoftAccount()` / … handlers, and VisuAuth
adds the pages, the first-time-user logic, and the runtime configuration overlay.

## Pages

| Route | Purpose |
|---|---|
| `/visuauth/external-login/start` | Challenges the chosen provider |
| `/visuauth/external-login/callback` | Handles the provider's return |
| `/visuauth/external-login/confirm` | First-time account confirmation, when the strategy asks for it |

The sign-in page renders a button per registered scheme. Wire no providers and
the whole "or sign in with" section suppresses itself — nothing to hide, nothing
to configure.

## Wiring a provider

Register the handler as you would in any ASP.NET Core app:

```csharp
var authentication = builder.Services.AddAuthentication();

authentication.AddGoogle(o =>
{
    o.ClientId = builder.Configuration["ExternalProviders:Google:ClientId"]!;
    o.ClientSecret = builder.Configuration["ExternalProviders:Google:ClientSecret"]!;
});
```

Keep secrets out of source — user-secrets in development, environment variables
or a vault in production. The reference wiring for all four providers lives in
[`samples/Sample.WebApp`](https://github.com/VisuAuth/VisuAuth/tree/main/samples/Sample.WebApp).

## First-time users

When someone signs in with a provider and has no local account yet,
`ExternalLoginOptions.FirstTimeStrategy` decides what happens:

| Strategy | Behaviour |
|---|---|
| `AutoCreate` *(default)* | Provisions a local user straight from the provider's claims. Frictionless. |
| `AutoLinkByEmailOrConfirm` | Links to an existing local account with the same email; otherwise asks the user to confirm. |
| `AlwaysConfirm` | Always shows the confirmation page before creating anything. |

```csharp
builder.Services.Configure<ExternalLoginOptions>(options =>
{
    options.FirstTimeStrategy = ExternalLoginFirstTimeStrategy.AutoLinkByEmailOrConfirm;
});
```

`AutoCreate` also marks the new user's email confirmed, on the grounds that the
provider already verified it. Flip that off if you want your own confirmation
step on top.

> **Pick deliberately.** `AutoLinkByEmailOrConfirm` links accounts on a matching
> email, which is convenient — and only as trustworthy as the provider's email
> verification. For providers that let users set an unverified email, prefer
> `AlwaysConfirm`.

## Managing credentials from the admin UI

`/visuauth/admin/external-providers` lets an operator fill in or rotate a
provider's client id and secret at runtime. Secrets are encrypted at rest with
ASP.NET Core Data Protection, and a save takes effect on the **next request** —
no restart.

Opt in per provider:

```csharp
builder.Services.AddVisuAuthExternalProviderConfigStore();
builder.Services.AddVisuAuthDynamicExternalProviderOptions<GoogleOptions>("Google");
```

The overlay reads the stored values and layers them over whatever the static
`AddGoogle(...)` call set, so the database wins when it has a value and your
configuration remains the fallback. The page badges which source each value came
from, so "is this the appsettings value or the one I typed?" is answerable at a
glance.

The screen also lists roughly twenty popular providers it knows about as
inactive cards, so an operator can see what's possible — activating one still
needs the matching `AddXxx()` handler registered in code, since VisuAuth can't
add an OAuth handler at runtime.

> **Pre-register the schemes you intend to manage.** A provider can only be
> configured from the UI if its handler exists. The sample registers all four
> schemes unconditionally (with empty credentials) precisely so an operator can
> enable one from scratch without a code change.

## Backends without external providers

Entra-backed deployments declare `SupportsExternalProviders = false` — Entra
*is* the identity provider, and its own federated logins (Google, Facebook,
Apple) are configured in the Entra tenant, not here. The section and the admin
page hide themselves accordingly. See
[Backend abstraction & capabilities](concepts/backend-abstraction.md).

> **Capture note.** This page has no screenshots yet — the external-providers
> admin capture is tracked in
> [`docs/CAPTURE_CHECKLIST.md`](https://github.com/VisuAuth/VisuAuth/blob/main/docs/CAPTURE_CHECKLIST.md).
