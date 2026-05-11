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
builder.Services.AddVisuAuth()
    .UseAspNetIdentity<ApplicationUser>()
    .EnableMultiTenant()
    .AddAdminUi()
    .AddEndUserUi();

app.MapVisuAuth();
```

That's it. Navigate to `/visuauth/admin` for the dashboard, `/visuauth/login` for the login page.

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
- `/visuauth/two-factor`
- `/visuauth/profile` — account self-management
- `/visuauth/logout`

### 🏢 Multi-tenancy
- `TenantId` column on `AspNetUsers` (and friends)
- Global query filter applied automatically
- Tenant resolution via subdomain, header, or JWT claim
- Per-tenant password policy, lockout, branding

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
| **0.2** | Microsoft Entra ID adapter, TOTP, external providers, view override theming | 📋 Planned |
| **0.3** | Entra External ID adapter, profile/sessions, audit log | 📋 Planned |
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

> Real getting-started docs will land alongside v0.1. Until then, this README is the source of truth.

## Contributing

Issues, discussions, and PRs welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[Apache License 2.0](LICENSE). Copyright © 2026 Thiago Luga and VisuAuth contributors.
