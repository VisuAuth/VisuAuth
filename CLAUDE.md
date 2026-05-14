# CLAUDE.md — VisuAuth

> Permanent project manual. Read at the start of every Claude session.
> Defines vision, architecture, conventions, and rules.
> Updated only when structural decisions change.

---

## 1. Product

**VisuAuth** is an open source NuGet package that fills the gap ASP.NET Core Identity leaves: a drop-in admin dashboard, multi-tenancy, and themeable end-user authentication pages — the same way Hangfire ships a dashboard for background jobs.

### 1.1 The problem we solve

ASP.NET Core Identity ships `UserManager<T>`, password hashing, lockout, and token providers — but no admin UI, no multi-tenancy, no professional end-user pages. Every team rebuilds the same fragments badly under deadline pressure. Microsoft's `Microsoft.AspNetCore.Identity.UI` is a 2017 Bootstrap scaffold that nobody loves and most teams scaffold-and-rewrite.

VisuAuth is the polished, drop-in alternative.

### 1.2 Positioning

| | They are | We are |
|---|---|---|
| Keycloak / Authentik | Full IAM servers with OIDC, federation, realms | A UI layer on top of the consumer's existing Identity |
| Duende IdentityServer | Token-issuing OIDC framework, paid | Not an OIDC server (we issue a simple JWT for mobile, that's it) |
| Auth0 / Clerk / Stytch | Cloud-hosted CIAM | Self-hosted, in the consumer's process and database |
| `Microsoft.AspNetCore.Identity.UI` | Microsoft's scaffolded Razor pages | Same niche, modern UX, multi-tenancy, themeable, mobile-ready, admin dashboard |

### 1.3 Audience

.NET developers who:

- Already chose ASP.NET Core Identity
- Want users in their own database
- Don't want to host an IAM server
- Need an admin UI for support and operations
- May serve multiple tenants from a single app

In v0.2+, also: teams using Microsoft Entra ID / Entra External ID who want a friendlier admin UI than the Azure Portal.

### 1.4 Distribution

NuGet meta-package `VisuAuth` plus granular packages (`VisuAuth.Abstractions`, `VisuAuth.Identity`, `VisuAuth.AdminUi`, `VisuAuth.EndUserUi`). Apache 2.0. Repository at <https://github.com/VisuAuth/visuauth>.

No paid tier in v1.0. Future commercial layer (hosted offering, enterprise support, paid adapters) is a possibility but not the current focus.

---

## 2. Architecture principles

### 2.1 Drop-in or nothing

Consumers add two lines to `Program.cs`:

```csharp
builder.Services.AddVisuAuth<ApplicationUser>();
app.MapVisuAuth();
```

That is the entire integration story. No Node.js, no build step on the consumer side, no manual middleware wiring, no Razor file copying, no scaffolding. If a design forces the consumer to do more, it is wrong.

### 2.2 Abstractions first

`IUserStore`, `IRoleStore`, `IAuthenticationFlow`, and `UserBackendCapabilities` exist in `VisuAuth.Abstractions` from day one, even though only the ASP.NET Identity adapter ships in v0.1.

The Entra ID and Entra External ID adapters that come in v0.2 and v0.3 plug into the same contracts. The UI consults capability flags at runtime to adapt: a backend that does not support local login (Entra) gets a "Sign in with Microsoft" button instead of an email/password form, automatically.

### 2.3 No JavaScript framework

Server-rendered Razor Pages plus htmx. No React, no Vue, no Blazor. Reasons:

- Consumers do not need a build pipeline
- Output is plain HTML — easy to inspect, theme, embed
- htmx (~15 KB) delivers reactive UX with HTML attributes
- Same model as Hangfire's dashboard, proven over years

### 2.4 Modular packages

| Package | Responsibility |
|---|---|
| `VisuAuth.Abstractions` | Contracts: `IUserStore`, `IRoleStore`, `IAuthenticationFlow`, capabilities, DTOs |
| `VisuAuth.Identity` | ASP.NET Core Identity adapter, multi-tenancy primitives |
| `VisuAuth.AdminUi` | Admin dashboard pages, layout, htmx, static assets |
| `VisuAuth.EndUserUi` | Login, register, password reset, profile, JWT issuance, mobile REST API |
| `VisuAuth` | Meta-package referencing everything above |

Consumers install the meta-package to get everything, or pick individual packages for finer control.

### 2.5 No vendor lock-in

VisuAuth does not store anything in a proprietary format. Users live in `AspNetUsers`. Roles live in `AspNetRoles`. Claims live in `AspNetUserClaims`. Optional VisuAuth-specific tables (such as `VisuAuthAuditLog` when the audit plugin is enabled) are explicit and documented. Uninstalling VisuAuth never destroys consumer data.

---

## 3. Tech stack

| Category | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 (LTS) | Latest LTS at v0.1 release |
| Language | C# 14 | Primary constructors, collection expressions, file-scoped namespaces |
| Web | ASP.NET Core 10 Razor Pages | Server-rendered HTML, drop-in friendly |
| UI interactivity | htmx 2.0.x | HTML-attribute-driven AJAX |
| Identity backend (v0.1) | ASP.NET Core Identity | The framework default we extend |
| Identity backend (v0.2+) | Microsoft Entra ID via Microsoft Graph | Adapter pattern |
| ORM | EF Core 10 | Identity stores already use it |
| Database providers | SQL Server / PostgreSQL / SQLite | Whatever the consumer chose for Identity |
| Mobile auth | JWT HS256 | Minimal token issuance, no IAM server complexity |
| Tests | xUnit + FluentAssertions + `WebApplicationFactory` | Standard .NET testing stack |
| CI | GitHub Actions | Free for OSS |
| License | Apache 2.0 | Enterprise-friendly, patent grant, no copyleft |

`Directory.Packages.props` enforces Central Package Management. Adding a new NuGet dependency requires a `PackageVersion` entry there.

---

## 4. Repository structure

```
visuauth/
├── CLAUDE.md                       # This file
├── PLAN.md                         # Working roadmap
├── README.md                       # Public-facing, used by NuGet + GitHub
├── LICENSE                         # Apache 2.0
├── CONTRIBUTING.md
├── SECURITY.md
├── .editorconfig
├── .gitattributes
├── .gitignore
├── global.json                     # Pins .NET 10 SDK
├── Directory.Build.props           # Shared MSBuild settings
├── Directory.Packages.props        # Central Package Management
│
├── src/
│   ├── VisuAuth.slnx               # Solution file (new .NET 10 XML format)
│   ├── VisuAuth/                   # Meta-package: AddVisuAuth, MapVisuAuth
│   ├── VisuAuth.Abstractions/      # Contracts (IUserStore, capabilities, DTOs)
│   ├── VisuAuth.Identity/          # ASP.NET Identity adapter, multi-tenancy
│   ├── VisuAuth.AdminUi/           # Admin dashboard Razor Pages + htmx + CSS
│   └── VisuAuth.EndUserUi/         # Login, register, password reset, JWT, mobile API
│
├── tests/
│   └── VisuAuth.AdminUi.Tests/     # Smoke tests via WebApplicationFactory
│
├── samples/
│   └── Sample.WebApp/              # Reference consumer app used in tests
│
└── .github/
    ├── workflows/
    │   ├── ci.yml                  # Build + test + pack on PR
    │   └── release.yml             # NuGet publish on git tag
    ├── PULL_REQUEST_TEMPLATE.md
    └── ISSUE_TEMPLATE/
```

---

## 5. Coding conventions

### 5.1 Language: English only

**All code, comments, documentation, commits, PR titles, branch names, and GitHub artifacts MUST be in English. No exceptions.**

This includes, but is not limited to:

- C# code: identifiers, comments, XML doc, error messages, log strings
- Razor templates and CSS class names
- HTML text content shipped with the library
- Markdown documentation: `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `PLAN.md`, `CLAUDE.md`, anything under `docs/`
- Git commit messages and pull request descriptions
- Branch names
- GitHub issues, labels, milestones, discussions
- Test names and any data baked into tests

Conversations between contributors can happen in any language; the project owner often discusses in Portuguese. Anything that lands on disk in this repository is in English.

### 5.2 C# style

- C# 14, file-scoped namespaces, nullable enabled, warnings as errors in Release
- `sealed` by default unless inheritance is intentional
- Primary constructors when they make the type shorter
- `record` for DTOs / value-like types, `class` for types with behavior
- `async`/`await` always with `CancellationToken` for any I/O
- `TimeProvider` instead of `DateTime.Now` / `DateTime.UtcNow`
- Result-style returns for expected errors (`UserResult.Failure(...)`), exceptions only for programmer errors and truly exceptional conditions
- One public type per file
- Members ordered: const, fields, ctor, properties, public methods, private methods
- See `.editorconfig` for the enforced rules

### 5.3 What we use

- ASP.NET Core Identity (`UserManager`, `SignInManager`, `RoleManager`) — in adapters only, never in `VisuAuth.Abstractions`
- EF Core for queries against Identity tables
- Razor Pages SDK (`Microsoft.NET.Sdk.Razor`) for the UI libraries
- htmx 2.0.x shipped as an embedded static asset under `VisuAuth.AdminUi/wwwroot/htmx.min.js` (no outbound CDN call — works in air-gapped deployments)
- xUnit for tests, FluentAssertions for assertions, `WebApplicationFactory` for integration

### 5.4 What we don't use

- **AutoMapper** — manual mapping. Our DTOs are small and the explicit code reads better.
- **MediatR** — not needed at this scale. Direct method calls on stores are fine.
- **Bootstrap, Tailwind, or any CSS framework** — own CSS with custom properties for theming.
- **React, Vue, Blazor** — anti-goal: keep the consumer pipeline simple.
- **JavaScript bundlers** (Vite, esbuild, webpack) — no JS build step at all.
- **AGPL or any copyleft license** — Apache 2.0 only.

---

## 6. Backend abstraction model

The single most important architectural decision. `VisuAuth.Abstractions` defines:

```csharp
public interface IUserStore
{
    UserBackendCapabilities Capabilities { get; }

    Task<UserSummary?> GetAsync(string id, CancellationToken ct = default);
    Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken ct = default);
    Task<UserResult> CreateAsync(CreateUserCommand command, CancellationToken ct = default);
    Task<UserResult> UpdateAsync(string id, UpdateUserCommand command, CancellationToken ct = default);
    Task<UserResult> DeleteAsync(string id, CancellationToken ct = default);
    Task<UserResult> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);
    Task<UserResult> ResetPasswordAsync(string id, CancellationToken ct = default);
    Task<UserResult> ResetTwoFactorAsync(string id, CancellationToken ct = default);
    Task<UserResult> RevokeSessionsAsync(string id, CancellationToken ct = default);
}
```

`UserBackendCapabilities` is a flag bag describing what the backend supports. The UI **must** consult capabilities and hide controls for unsupported operations. Methods marked as unsupported should throw `NotSupportedException` rather than silently succeed.

This is what enables v0.2's Entra ID adapter to plug in without a UI rewrite. Entra does not let us own the login form; the adapter declares `SupportsLocalLogin = false` and the end-user UI swaps the form for a "Sign in with Microsoft" button automatically.

Same pattern applies to `IRoleStore` and `IAuthenticationFlow`.

---

## 7. Multi-tenancy

### 7.1 Strategy

Shared database, shared schema, discriminator by `TenantId`. The simplest model that supports the majority of SaaS use cases.

### 7.2 Implementation

- A `TenantId` (nullable string) column is added to `AspNetUsers` via a custom `IdentityUser` subclass.
- `IMultiTenantEntity` marker interface lives in `VisuAuth.Identity.MultiTenancy`.
- An EF Core global query filter on `IMultiTenantEntity` automatically restricts queries to the current tenant.
- A `SaveChanges` interceptor auto-populates `TenantId` on insert.
- `ITenantContext` is resolved by middleware from either a subdomain (`tenant.app.example.com`), a header (`X-Tenant-Id`), or a JWT claim (`tenant_id`).
- Per-tenant configuration (password policy, lockout, branding) lives in `TenantSettings` and is consulted via `IPasswordPolicyResolver` and friends.

### 7.3 Default mode: single-tenant

Multi-tenancy is opt-in. Without `EnableMultiTenant()`, `ITenantContext.IsMultiTenancyEnabled` returns `false` and the query filters are no-ops.

---

## 8. UI architecture

### 8.1 Razor Pages as a library

`VisuAuth.AdminUi` and `VisuAuth.EndUserUi` are Razor Class Libraries (`<Sdk>Microsoft.NET.Sdk.Razor</Sdk>`). Pages are discovered by the host through `services.AddRazorPages().AddApplicationPart(typeof(...).Assembly)`, called from our DI extensions.

Routes are explicit via the `@page "/visuauth/..."` directive at the top of each `.cshtml`. Folder structure inside `Pages/` is for organization only and does not influence routing.

`_ViewStart.cshtml` and `_ViewImports.cshtml` must live at `Pages/` root (Razor cascades them down to subfolders). `_Layout.cshtml` lives in `Pages/Shared/`.

### 8.2 Static assets

`wwwroot/` content in a Razor Class Library is mounted by the host at `/_content/{AssemblyName}/...`. Example: `wwwroot/visuauth.css` in `VisuAuth.AdminUi` becomes `/_content/VisuAuth.AdminUi/visuauth.css` in the consumer's app. The consumer must have `app.UseStaticFiles()` (almost all do).

### 8.3 htmx integration

For each page that needs reactive behavior:

- The full GET returns the entire page with layout.
- When the request has the `HX-Request` header, the page returns only the partial (`return Partial("_X", this)`) — htmx swaps it in place.
- Search inputs use `hx-trigger="keyup changed delay:300ms"` for debounced live search.
- Pagination links also use htmx (`hx-get`, `hx-target`, `hx-push-url`) so back/forward navigation works.

### 8.4 Theming layers

Four layers of customization, ranked from simplest to most powerful:

1. **CSS custom properties** — consumer overrides `--visuauth-primary`, `--visuauth-bg`, etc. in their own stylesheet loaded after ours. Default theme is in `wwwroot/visuauth.css`.
2. **Programmatic config** — `services.AddVisuAuth().Configure<VisuAuthTheme>(...)`. Generates CSS variables at runtime.
3. **View override** — consumer drops their own `.cshtml` in `/Views/VisuAuth/` (or a folder configured via `VisuAuthViewOverrideOptions.Root`) and ours falls back if absent. Two mechanisms cooperate: an `IViewLocationExpander` prepends the override folder to Razor's view-engine search list for partials and layouts; a `DemoteVisuAuthPagesConvention` sets `AttributeRouteModel.Order = 1000` on every VisuAuth Razor Page so a consumer page in the host app at the same `@page` route wins via the lower-order-wins rule.
4. **Per-tenant overrides** — consumers implement two resolver contracts that VisuAuth consults on every render once multi-tenancy is on:
   - **`ITenantThemeResolver`** returns a `VisuAuthTheme?` keyed off `ITenantContext.CurrentTenantId`. The `<va-theme-style />` tag helper overlays the result on top of the global `IOptions<VisuAuthTheme>` via `VisuAuthThemeMerger.Merge` — tenant wins per property, global fills the rest, anything still null falls through to the CSS defaults.
   - **`ITenantViewOverrideResolver`** returns a per-tenant override root (e.g. `/Views/VisuAuth/Tenants/acme`) — a same-named `.cshtml` there wins ahead of both the global layer-3 root and the package defaults, so partials and layouts can vary per tenant. The expander stashes the resolved tenant id in Razor's view-location cache key so tenant A's swapped template never shadows tenant B's on the next request.
   Default registrations (`NoOp*Resolver`) return null so consumers who never opt in keep the layer-2 / layer-3 fast paths. Per-tenant whole-page overrides (different consumer Razor Page per tenant at the same route) need a custom `EndpointSelectorPolicy` and are out of scope here.

---

## 9. Mobile support

### 9.1 Two flows, same backend

**Flow 1 (REST API):** mobile app builds its own UI, posts credentials to `/visuauth/api/auth/login`, receives a JWT. Used when the app wants a 100% native UX.

**Flow 2 (WebView):** mobile app opens an in-app browser pointing to `/visuauth/login?return=app://callback`. After authentication, the server redirects to the deep link with the JWT. Used when the app wants the same themed pages as the web flow, including external providers.

Both flows share the same `IAuthenticationFlow` implementation and the same JWT issuer. The only difference is how the request arrives.

### 9.2 JWT

- HS256 with a symmetric key from configuration (`VisuAuth:Jwt:SigningKey`)
- Claims: `sub` (user id), `email`, `tenant_id` (if multi-tenant), `roles`, `exp`
- Default lifetime: 1 hour, configurable
- No discovery endpoint, no JWKS, no rotation in v0.1 (would re-introduce IAM server complexity)
- Consumers who need OIDC should pair VisuAuth with Duende IdentityServer or similar — VisuAuth does not aim to replace those

---

## 10. Testing

### 10.1 Layout

Two projects under `tests/`:

```
tests/
├── VisuAuth.UnitTests/         # fast, in-memory. xUnit + FluentAssertions + Moq
└── VisuAuth.IntegrationTests/  # WebApplicationFactory<Sample.WebApp.Program>
```

**Rule of thumb for picking a project:**

| Criterion | UnitTests | IntegrationTests |
|---|---|---|
| Boots an HTTP server | ❌ | ✅ |
| Touches the DbContext / SQLite | ❌ | ✅ |
| Renders Razor / asserts on HTML | ❌ | ✅ |
| Target time per test | < 5 ms | 30–100 ms |

Inside each project, sub-folders mirror `src/`:
`Abstractions/`, `Identity/Users/`, `Identity/Roles/`, `Identity/Authentication/`,
`Identity/MultiTenancy/`, `Admin/`, `EndUser/`, `Api/`, `MultiTenancy/`.

### 10.2 Naming

**`Method_Scenario_ExpectedResult`** — three underscored parts.

Unit tests use the method-under-test as the first part:
`Generate_WithUppercaseRequired_IncludesAtLeastOneUppercase`.

Integration tests use HTTP verb + endpoint as the first part (the
"method" being exercised is the endpoint):
`PostLogin_WithWrongPassword_Returns401WithoutToken`,
`GetEditRole_OnExistingRole_RendersRowInEditMode`.

### 10.3 Tooling

- **xUnit** test runner
- **FluentAssertions** for `Should().Contain(...)` etc.
- **Moq** for mocks in unit tests (no NSubstitute — single mocking library
  by convention)
- **`Microsoft.AspNetCore.Mvc.Testing`** + the sample app for integration
- **`InternalsVisibleTo("VisuAuth.UnitTests")`** on adapter projects so
  unit tests can reach internal helpers (`TemporaryPasswordGenerator`, etc.)

### 10.4 Rules

- Every bug fix ships with a regression test.
- New behaviour without a test is rejected.
- Tests must run in any order — never depend on prior test state. Provision
  throwaway users / roles with `Guid.NewGuid()`-suffixed names when mutating.
- The SQLite DB is shared between integration-test classes; assembly-level
  `DisableTestParallelization = true` keeps them serialised.
- Coverage target (post-1.0): 80% on `VisuAuth.Identity` and the public
  surface of `VisuAuth.Abstractions`.

### 10.5 Local code-quality scan (SonarQube)

The repository ships a Docker Compose stack with SonarQube Community + Postgres
so issues are caught locally before a PR is opened. The same rules / quality
gate also run on CI against SonarCloud — local is just a faster feedback loop.

**One-time setup**

1. Start the stack:

   ```powershell
   docker compose -f docker-compose.sonar.yml up -d
   ```

2. Wait ~1 minute, open <http://localhost:9000>, login with `admin` / `admin`,
   and change the password.
3. Generate a user token: *My Account → Security → Generate*.
4. Persist the token in the environment:

   ```powershell
   setx SONAR_TOKEN "<the-token>"
   ```

   Open a **new** terminal so the variable is visible.

**Run the scan**

```powershell
scripts/sonar-local.ps1
```

The script runs `begin → build → tests with OpenCover coverage → end` against
the local instance, then prints
<http://localhost:9000/dashboard?id=VisuAuth>.

> SonarQube Community Edition analyses **only the main branch view** — every
> local run overwrites the previous one. That is intentional: the local
> instance answers "would this change pass the gate today?", nothing more.

---

## 11. Branching and commits

See `CONTRIBUTING.md` for the full policy. Quick reference:

- Trunk-based: `main` is the only long-lived branch.
- Branch prefixes: `feat/`, `fix/`, `docs/`, `chore/`, `refactor/`, `test/`, `perf/`, `ci/`, `build/`, `release/`, `hotfix/`.
- [Conventional Commits](https://www.conventionalcommits.org) for commits and PR titles.
- **Squash merge only.** PR title becomes the squashed commit message on `main`.
- The very first commit of the repository was allowed in `main` directly (bootstrap). Every commit since then goes through a PR.

---

## 12. Rules for Claude

When working on this repository:

- **English everywhere.** Every file you create, every comment you write, every commit message, every PR title — in English. If the project owner messages you in Portuguese, you may respond in Portuguese. Anything that lands on disk is in English.
- **Never run `git add`, `git commit`, `git push`, `gh pr create`, or any destructive git command.** Staging and git operations are the project owner's responsibility. Claude reads, writes files, and runs `dotnet build` / `dotnet test`; nothing else with git.
- **Never publish to NuGet.** Pushes to `nuget.org` happen through CI on a git tag, or by the owner manually. Claude does not invoke `dotnet nuget push`.
- **Don't introduce new NuGet dependencies without asking.** New packages require a `PackageVersion` entry in `Directory.Packages.props` and a clear justification.
- **Don't break the drop-in promise.** If a change would force consumers to do more than `AddVisuAuth<TUser>()` + `MapVisuAuth()`, push back.
- **Keep abstractions backend-agnostic.** `VisuAuth.Abstractions` knows nothing about `UserManager`, EF Core, or Microsoft Graph. Only DTOs and contracts.
- **Razor Pages live in libraries.** Pages in `VisuAuth.AdminUi` and `VisuAuth.EndUserUi` must work as referenced packages, not only as host-app pages. Always verify by running the sample app and the smoke tests.
- **Treat the sample app as a contract.** If something works only in the sample but not when published as a NuGet, the package is broken.
- **Always run `dotnet build src/VisuAuth.slnx -c Release` and `dotnet test src/VisuAuth.slnx -c Release` after non-trivial changes.** Both must pass before reporting "done".
- **Run the local SonarQube scan after any non-trivial C# change.** After build / tests pass, run `scripts/sonar-local.ps1` (the Compose stack must be up — see section 10.5), open the dashboard at <http://localhost:9000/dashboard?id=VisuAuth>, and **fix the new bugs, vulnerabilities, and code smells you introduced** before reporting "done". Pre-existing issues on unrelated code are out of scope; surface them to the owner instead of silently fixing them. If the stack is not running or `SONAR_TOKEN` is unset, say so explicitly in the final summary instead of skipping silently. Pure docs / CI-config / Markdown changes are exempt.
- **Don't add features the owner did not ask for.** Stay focused on the PR scope.
- **Multi-tenancy applies to every user-scoped entity.** When adding a new entity, ask whether it needs `TenantId` (almost always yes).
- **When in doubt about a UI choice, look at Hangfire's dashboard.** Same drop-in spirit, mature design.
- **Use `TimeProvider`, never `DateTime.Now` or `DateTime.UtcNow`.**
- **Use `Result`-style returns for expected errors.** No exceptions for validation or business rule violations.
- **Use the architecture and conventions in this document. When you find yourself wanting to deviate, ask first.**

---

## 13. Roadmap

| Version | Scope | Status |
|---|---|---|
| 0.0.1-alpha | Placeholder NuGet release reserving the name | ✅ Shipped |
| 0.1 | Admin UI (CRUD users, roles, lockout, reset), end-user UI (login, register, reset, confirm, profile), multi-tenancy, theming layers 1+2+3+4, mobile REST + JWT, WebView flow, i18n (pt-BR + en), embedded htmx asset | 🚧 In progress |
| 0.2 | Microsoft Entra ID adapter, TOTP pages, external providers (Google, Microsoft, Apple), audit log plugin | 📋 Planned |
| 0.3 | Microsoft Entra External ID adapter, profile / sessions management, bulk operations, view-level customization | 📋 Planned |
| 1.0 | Production-ready, stable contracts, full English documentation site | 📋 Planned |

Track concrete next steps in `PLAN.md`.

---

## 14. Glossary

| Term | Definition |
|---|---|
| Consumer | The application that installs the VisuAuth NuGet package |
| Tenant | A logical partition within the consumer's app, when multi-tenancy is enabled |
| Backend | The identity store implementation: ASP.NET Identity, Entra ID, etc. |
| Adapter | A package that implements VisuAuth's abstractions against a specific backend |
| Capabilities | The `UserBackendCapabilities` record describing what a backend supports |
| Drop-in | The promise that `AddVisuAuth<TUser>` + `MapVisuAuth` is the entire integration |
| Admin UI | The dashboard mounted at `/visuauth/admin/...` for operators |
| End-user UI | The pages at `/visuauth/login`, `/visuauth/register`, etc. for the consumer's end users |
| Sample app | `samples/Sample.WebApp` — the reference consumer, also used in integration tests |
| Meta-package | The `VisuAuth` NuGet that depends on all the others, for one-line install |
