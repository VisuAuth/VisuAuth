# VisuAuth

**The visual admin for ASP.NET Core Identity.**

VisuAuth fills the gap that ASP.NET Core Identity leaves: a drop-in **admin
dashboard** for users, roles, and claims; a **multi-tenancy** layer with
per-tenant isolation; and **themeable end-user authentication pages** — all
wired in two lines of code, the same way Hangfire ships its dashboard.

```csharp
// Register the admin UI + end-user pages, then map the routes.
builder.Services.AddVisuAuth<ApplicationUser>();
app.MapVisuAuth();
```

Navigate to `/visuauth/admin` for the dashboard and `/visuauth/login` for the
sign-in page.

> The sign-in pages and the mobile API also need a JWT issuer
> (`AddVisuAuthJwt<TUser>(…)`) — the **[Getting started](getting-started.md)**
> guide shows the complete, runnable setup.

![VisuAuth admin dashboard](assets/screenshots/admin-dashboard.png)

> **New here?** Jump straight to the **[Getting started](getting-started.md)**
> guide — it takes you from an empty project to a working `/visuauth/admin`.

## See it working

Live search that filters as you type — server-rendered Razor with htmx, no
JavaScript framework and no build step on your side:

![Typing in the search box filters the admin user list live, without a page reload](assets/screenshots/admin-user-list-search.gif)

Light and dark ship built in, following the OS with a toggle that remembers the
visitor's choice:

![The admin dashboard switching between light and dark themes](assets/screenshots/theming-light-dark.gif)

More of the UI, screen by screen, in **[the admin UI tour](admin-ui.md)**.

## What you get

| Surface | Highlights |
|---|---|
| **Admin UI** (`/visuauth/admin`) | Dashboard, list / search / filter users with pagination, create / edit / lock / delete, reset password, revoke sessions, reset 2FA, roles, tenants, audit log. Locked by default. See [the tour](admin-ui.md). |
| **End-user UI** | `/visuauth/login`, `/visuauth/register`, `/visuauth/forgot-password`, `/visuauth/reset-password`, `/visuauth/confirm-email`, `/visuauth/two-factor/*`, `/visuauth/external-login/*`. |
| **Multi-tenancy** | `TenantId` discriminator on the Identity tables, automatic global query filter, and tenant resolution from the signed JWT claim, a header, or the admin's cookie switcher. See [Multi-tenancy](concepts/multi-tenancy.md). |
| **Theming** | Four layers — CSS tokens, programmatic `VisuAuthTheme`, Razor view overrides, per-tenant resolvers. See [Theming](theming.md). |
| **Mobile-ready** | REST API at `/visuauth/api/auth` with HS256 JWT issuance, opaque rotating refresh tokens, plus a WebView deep-link flow. See [Mobile & JWT](mobile.md). |
| **Pluggable backend** | ASP.NET Core Identity, Microsoft Entra ID, and Microsoft Entra External ID — all behind the same contracts. See [Backend abstraction](concepts/backend-abstraction.md). |

Self-service profile management (`/visuauth/profile`) is planned, not shipped —
see the [roadmap](https://github.com/VisuAuth/VisuAuth#roadmap).

## Why it exists

ASP.NET Core Identity gives you `UserManager<T>`, password hashing, lockout,
and token providers — but **no admin UI, no multi-tenancy, no professional
end-user pages**. Every team rebuilds the same fragments under deadline
pressure. VisuAuth is to Identity what **Hangfire is to background jobs**: the
operational surface the framework should have shipped with.

## Positioning

VisuAuth is **not** an identity server. It does not replace Keycloak,
Duende IdentityServer, or Auth0. It is a **UI and operations layer on top of
the Identity you already run**, in your own process and database — no extra
server to host, no proprietary data store, no vendor lock-in. Users stay in
`AspNetUsers`, roles in `AspNetRoles`, claims in `AspNetUserClaims`.

## Install

```bash
dotnet add package VisuAuth
```

The meta-package pulls in `VisuAuth.Abstractions`, `VisuAuth.Identity`,
`VisuAuth.AdminUi`, and `VisuAuth.EndUserUi`. Prefer a subset? Install the
individual `VisuAuth.*` packages instead.

Then follow the **[Getting started](getting-started.md)** guide. For the
security model — per-flow threats, mitigations, and where each is enforced and
tested — see the **[Security posture](security.md)**.

---

Apache 2.0 licensed. Source on [GitHub](https://github.com/VisuAuth/VisuAuth).
