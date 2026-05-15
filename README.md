<div align="center">

# VisuAuth

**The visual admin for ASP.NET Core Identity.**

Drop-in admin dashboard, multi-tenancy, and themeable end-user auth pages — without forcing you to rebuild what the framework should ship with.

[![NuGet](https://img.shields.io/nuget/v/VisuAuth.svg)](https://www.nuget.org/packages/VisuAuth/)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

</div>

> 🚧 **Pre-alpha.** The current NuGet release is a placeholder reserving the package name. Real releases will follow as the project matures.

## What it is

VisuAuth fills the gap that ASP.NET Core Identity leaves: it ships a complete admin UI for users/roles/claims, a multi-tenancy layer with per-tenant isolation, and themeable end-user authentication pages — all drop-in via two lines of code, the same way Hangfire ships its dashboard.

```csharp
// Two-line drop-in — the form recommended for most consumers.
builder.Services.AddVisuAuth<ApplicationUser>();

app.MapVisuAuth();
```

Need finer control? The same call has a fluent form so you can pick the
surfaces you want:

```csharp
builder.Services.AddVisuAuth()
    .UseAspNetIdentity<ApplicationUser>()
    .AddAdminUi()
    .AddEndUserUi();
```

Either way, navigate to `/visuauth/admin` for the dashboard and
`/visuauth/login` for the login page. A complete sample, including the
EF Core / Identity wiring this snippet leaves out, lives in
[`samples/Sample.WebApp`](samples/Sample.WebApp).

## What it gives you

### 🖥️ Admin UI (`/visuauth/admin`)
- List, search, filter users with pagination
- Create, edit, lock, unlock, delete users
- Reset password, force logout, reset 2FA
- Manage roles and claims
- Per-tenant scoped views

### 🌐 End-user UI
- `/visuauth/login` — email + password
- `/visuauth/register` — self-service signup (configurable)
- `/visuauth/forgot-password` & `/visuauth/reset-password`
- `/visuauth/confirm-email`
- `/visuauth/logout`
- `/visuauth/two-factor` — *coming in v0.2*
- `/visuauth/profile` — account self-management, *coming in v0.3*

### 🏢 Multi-tenancy
- `TenantId` column on `AspNetUsers` (and friends)
- Global query filter applied automatically
- Tenant resolution via HTTP header (`X-Tenant-Id`) or cookie set by the
  admin sidebar switcher (subdomain and JWT-claim resolvers planned for v0.2)
- Per-tenant branding via the theming layers below
  (per-tenant password policy / lockout planned for v0.2)

### 🎨 Theming
- **Layer 1:** Override CSS variables — colors, fonts, logo, radius
- **Layer 2:** Programmatic config via `VisuAuthTheme`
- **Layer 3:** Razor view override for granular control
- **Layer 4:** Per-tenant theme resolved at runtime

### 📱 Mobile-ready
- REST API at `/visuauth/api/auth` with JWT (HS256) issuance
- WebView flow with deep-link callback for native apps

### 🔌 Pluggable backend
- v0.1: ASP.NET Core Identity
- v0.2: Microsoft Entra ID (admin UI via Microsoft Graph)
- v0.3: Microsoft Entra External ID

Abstractions (`IUserStore`, `IRoleStore`, `IAuthenticationFlow`, `UserBackendCapabilities`) are shaped from day 1 so adapters plug in cleanly.

## Why VisuAuth exists

ASP.NET Core Identity gives you `UserManager<T>`, hashing, lockout, and token providers — but **no admin UI, no multi-tenancy, no professional end-user pages**. Every team rebuilds the same fragments, badly, under deadline pressure.

VisuAuth is to Identity what **Hangfire is to background jobs**: it adds the dashboard and operational surface that the framework should have shipped with.

## Stack

| | |
|---|---|
| Runtime | .NET 10 |
| Web | ASP.NET Core Razor Pages |
| Frontend | htmx (no JS framework, no build step on the consumer side) |
| Identity | ASP.NET Core Identity (v0.1); Microsoft Entra ID / External ID (v0.2+) |
| Storage | EF Core (SQL Server, PostgreSQL, SQLite) |
| Mobile | REST API + JWT (HS256) |
| i18n | pt-BR, en (more on request) |
| License | Apache 2.0 |

## Roadmap

| Version | Scope | Status |
|---|---|---|
| **0.0.1-alpha** | Placeholder, name reserved | ✅ Released |
| **0.1** | Admin UI + End-user UI + Multi-tenancy + Theming + Mobile | 🚧 In development |
| **0.2** | Microsoft Entra ID adapter, TOTP, external login providers, audit log | 📋 Planned |
| **0.3** | Entra External ID adapter, profile / sessions management, bulk operations | 📋 Planned |
| **1.0** | Production-ready, stable contracts | 📋 Planned |

## Repository structure

```
visuauth/
├── src/
│   ├── VisuAuth/                  # Meta-package
│   ├── VisuAuth.Abstractions/     # IUserStore, capabilities, contracts
│   ├── VisuAuth.Identity/         # ASP.NET Identity adapter
│   ├── VisuAuth.AdminUi/          # Admin dashboard
│   └── VisuAuth.EndUserUi/        # Login, register, password reset, etc.
├── tests/
├── samples/
│   └── Sample.WebApp/             # Drop-in example
└── docs/                          # (Docusaurus, coming soon)
```

## Getting started

VisuAuth assumes a project that already has ASP.NET Core Identity + EF Core
configured. The reference setup lives in
[`samples/Sample.WebApp`](samples/Sample.WebApp); the section below is the
quickest path from an empty project to a working `/visuauth/admin`.

### 1. Install

```bash
dotnet add package VisuAuth
```

That meta-package pulls in `VisuAuth.Abstractions`, `VisuAuth.Identity`,
`VisuAuth.AdminUi`, and `VisuAuth.EndUserUi` as transitive dependencies.
Need only a subset? Install the individual `VisuAuth.*` packages instead.

### 2. Identity user, DbContext, migrations

Define an Identity user and the DbContext that owns it. The single-tenant
form is fine for v0.1 even if you plan to opt into multi-tenancy later —
swap `IdentityUser` for `MultiTenantIdentityUser` and
`IdentityDbContext<TUser>` for `MultiTenantIdentityDbContext<TUser>` when
you do.

```csharp
public sealed class ApplicationUser : IdentityUser { }

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options);
```

Run `dotnet ef migrations add Initial && dotnet ef database update`
(or your equivalent) to create the `AspNet*` tables. VisuAuth does not
add tables of its own in v0.1.

### 3. Wire VisuAuth in `Program.cs`

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VisuAuth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Two-line drop-in — registers Identity adapter + admin UI + end-user UI.
builder.Services.AddVisuAuth<ApplicationUser>();

var app = builder.Build();

app.UseStaticFiles();    // serves /_content/VisuAuth.AdminUi/visuauth.css etc.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapVisuAuth();

app.Run();
```

### 4. Try it

- `/visuauth/admin` — the dashboard. Restrict it with your own
  authorization policy (`[Authorize(Roles = "Admin")]` on a custom
  layout filter, an endpoint convention, etc.) before going to
  production — VisuAuth does not impose one in v0.1.
- `/visuauth/login` — the public sign-in page.
- `/visuauth/register`, `/visuauth/forgot-password`,
  `/visuauth/reset-password`, `/visuauth/confirm-email`.

For multi-tenancy, mobile JWT issuance, theming (CSS variables, programmatic
`VisuAuthTheme`, view overrides, per-tenant resolvers), and the i18n
pipeline, see
[`samples/Sample.WebApp/Program.cs`](samples/Sample.WebApp/Program.cs) — it
exercises every public surface.

## Building & packaging

The repo packs five NuGet artefacts in one go:
`VisuAuth`, `VisuAuth.Abstractions`, `VisuAuth.Identity`, `VisuAuth.AdminUi`, `VisuAuth.EndUserUi`.
Every package shares the same version, sourced from a single
`<VersionPrefix>` in [`Directory.Build.props`](Directory.Build.props).

### Local build

```bash
# Restore, build, test, all at once.
dotnet build src/VisuAuth.slnx --configuration Release
dotnet test  src/VisuAuth.slnx --configuration Release
```

### Local pack (smoke-test a new package without publishing)

```bash
# Pre-release with a local marker — never collides with anything on NuGet.org.
dotnet pack src/VisuAuth.slnx \
  --configuration Release \
  --output ./artifacts

# → ./artifacts/VisuAuth.0.1.0-alpha.local.nupkg (and the four sibling packages)
```

Override the suffix to mimic a CI build locally:

```bash
dotnet pack src/VisuAuth.slnx -c Release -o ./artifacts \
  -p:VersionSuffix=alpha.999
# → VisuAuth.0.1.0-alpha.999.nupkg
```

Override the full version to mimic a stable release:

```bash
dotnet pack src/VisuAuth.slnx -c Release -o ./artifacts \
  -p:Version=0.2.0 -p:VersionSuffix=
# → VisuAuth.0.2.0.nupkg
```

### Publish channels

Two release channels run off the same GitHub Actions workflow
([`.github/workflows/release.yml`](.github/workflows/release.yml)):

| Trigger | Version | NuGet visibility | How to consume |
|---|---|---|---|
| Push to `main` (every merge) | `0.1.0-alpha.<run_number>` | Pre-release (hidden by default) | `dotnet add package VisuAuth --prerelease` |
| Git tag matching `v*` (e.g. `v0.1.0`) | `0.1.0` (stable) | Stable, shows as "Latest" | `dotnet add package VisuAuth` |
| Manual `workflow_dispatch` | Same as push-to-main | Pre-release | — |

The version comes from `Directory.Build.props`'s `<VersionPrefix>` plus a
suffix computed by the workflow:

- On `main`: `-p:VersionSuffix=alpha.${{ github.run_number }}` — monotonic
  across the repo lifetime, so `0.1.0-alpha.42` < `0.1.0-alpha.43`.
- On tag: the tag name (minus the leading `v`) becomes the full version
  via `-p:Version=...`.

Both flows run `restore → build → test → pack → push`. The push step uses
`dotnet nuget push --skip-duplicate`, so a re-run never fails on a package
that already exists.

### Cutting a stable release

1. Bump `<VersionPrefix>` in `Directory.Build.props` if needed
   (e.g. starting work on `0.2.x`).
2. Land the release commit on `main`.
3. Tag and push:
   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```
4. The Release workflow detects `refs/tags/v0.2.0`, builds
   `VisuAuth.0.2.0.nupkg` (no suffix), and publishes to nuget.org.

### Repository secrets

| Secret | Used by | How to set |
|---|---|---|
| `NUGET_API_KEY` | Release workflow (`dotnet nuget push`) | Generate a key on [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys) with scope **Push new packages and package versions** for `VisuAuth*`, then add it under **Settings → Secrets and variables → Actions** as a repository secret. |

No other secrets are required — source-link metadata and license expression
are embedded at build time from [`Directory.Build.props`](Directory.Build.props).

## Contributing

Issues, discussions, and PRs welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[Apache License 2.0](LICENSE). Copyright © 2026 Thiago Luga and VisuAuth contributors.
