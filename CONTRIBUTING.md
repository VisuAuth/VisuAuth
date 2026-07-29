# Contributing to VisuAuth

Thanks for your interest. VisuAuth is pre-alpha and the surface is moving — issues, discussion, and PRs are all welcome.

## Quick start

```bash
git clone https://github.com/VisuAuth/visuauth.git
cd visuauth
dotnet restore src/VisuAuth.slnx
dotnet build src/VisuAuth.slnx
dotnet test src/VisuAuth.slnx
```

> The solution file lives in `src/VisuAuth.slnx`. Open that one in Visual Studio or Rider.

Requires the **.NET 10 SDK** (10.0.300 or later, matching `global.json`).

## Project layout

- `src/VisuAuth.Abstractions/` — Public contracts (`IUserStore`, `IRoleStore`, `UserBackendCapabilities`, etc.).
- `src/VisuAuth.Identity/` — ASP.NET Core Identity adapter (the default backend).
- `src/VisuAuth.AdminUi/` — Admin dashboard (Razor Pages + htmx).
- `src/VisuAuth.EndUserUi/` — Login, register, password reset, two-factor, external logins.
- `src/VisuAuth/` — Meta-package depending on the four above.
- `src/VisuAuth.EntraCore/` — Plumbing shared by both Entra adapters (Graph client, no-op stubs).
- `src/VisuAuth.Entra/` + `src/VisuAuth.Entra.Web/` — Entra ID (Workforce) adapter, and its operator OIDC sign-in.
- `src/VisuAuth.EntraExternal/` + `src/VisuAuth.EntraExternal.Web/` — Entra External ID (CIAM) adapter, and its customer OIDC sign-in.
- `samples/Sample.WebApp/` — Reference consumer, and the app the integration tests boot.
- `samples/Sample.EntraWebApp/`, `samples/Sample.EntraExternalWebApp/` — Minimal per-adapter references.

The `.Web` split keeps `Microsoft.Identity.Web` out of the core adapters, so
consumers who front the dashboard with their own authentication don't carry it.

## Branching and commits

VisuAuth uses **trunk-based development**: `main` is the only long-lived branch, and every change lands through a short-lived branch + pull request. We follow [Conventional Commits](https://www.conventionalcommits.org) for commit messages and PR titles.

### Branch naming

```
<type>/<short-description>
<type>/<issue-number>-<short-description>     # when an issue exists
```

| Prefix | Use for | Example |
|---|---|---|
| `feat/` | New user-visible functionality | `feat/admin-ui-user-search` |
| `fix/` | Bug fix | `fix/identity-tenant-filter-leak` |
| `docs/` | Documentation only | `docs/getting-started-guide` |
| `refactor/` | Restructure without behavior change | `refactor/extract-user-mapper` |
| `test/` | Tests only | `test/identity-adapter-coverage` |
| `perf/` | Performance improvement | `perf/cache-tenant-resolver` |
| `chore/` | Maintenance, deps, configs | `chore/bump-nuget-deps` |
| `ci/` | GitHub Actions changes | `ci/add-codeql-scan` |
| `build/` | Build system, project files | `build/enable-deterministic-builds` |
| `release/` | Release preparation | `release/v0.1.0` |
| `hotfix/` | Urgent fix from a published tag | `hotfix/v0.1.1-jwt-validation` |

Rules:

- Lowercase, hyphen-separated. No spaces, no underscores, no special chars.
- No personal names in branches (no `thiago/...`).
- Reference an issue number when one exists: `feat/42-add-totp-pages` — GitHub auto-links it.
- Keep the description under ~50 characters.
- Branches are short-lived. If a branch is open for more than a few days, rebase it on `main` to avoid drift.

### Commit messages

```
<type>(<scope>): <subject>

<optional body>

<optional footer>
```

Types match the branch prefixes above. Scope is a short module name when useful (`admin-ui`, `identity`, `abstractions`, `end-user-ui`, `ci`, etc.). Subject is imperative, lowercase, no trailing period.

Examples:

```
feat(admin-ui): add user list page with pagination
fix(identity): apply tenant filter on bulk delete
refactor(abstractions): split IUserStore from IAuthenticationFlow
chore: bump aspnetcore to 10.0.1
docs(readme): document the drop-in flow
```

A breaking change is marked with `!` and a `BREAKING CHANGE:` footer:

```
feat(identity)!: rename IUserStore.ListAsync to QueryAsync

BREAKING CHANGE: IUserStore.ListAsync was renamed to QueryAsync to match
the EF Core idiom. Adapters must be updated.
```

### Merge strategy

We use **squash merge** exclusively. Your PR's commits are squashed into a single commit on `main` whose message is the PR title (which should follow Conventional Commits). Keep the PR title clean and you don't have to worry about cleaning individual commits.

## Code style

- **Nullable reference types** are enabled and required.
- **Warnings as errors** in `Release` builds.
- **File-scoped namespaces** always.
- **`sealed` by default** unless inheritance is intentional.
- **`async`/`await` always with `CancellationToken`** for any I/O.
- **`TimeProvider`** instead of `DateTime.Now`/`DateTime.UtcNow`.
- **Records for DTOs**, classes for behavior.
- 1 file = 1 public type.
- **`using` directives are flat** — no blank lines between them, no grouping
  by namespace prefix. `dotnet_separate_import_directive_groups = false`
  enforces this in `.editorconfig`. Example:

  ```csharp
  using System.Net;
  using FluentAssertions;
  using Microsoft.AspNetCore.Mvc.Testing;
  using VisuAuth.Identity.MultiTenancy;
  using Xunit;
  ```

## Tests

Two test projects under `tests/`:

| Project | Purpose | Speed | Tooling |
|---|---|---|---|
| `VisuAuth.UnitTests` | Pure logic, no I/O, no host | < 5 ms / test | xUnit + FluentAssertions + Moq |
| `VisuAuth.IntegrationTests` | End-to-end via the sample app | 30–100 ms / test | + `WebApplicationFactory<Program>` |

Inside each project, sub-folders mirror `src/` so the location of a test
hints at what it covers (`Identity/Users/`, `Admin/`, `EndUser/`, `Api/`,
`MultiTenancy/`, etc.).

### Naming: `Method_Scenario_ExpectedResult`

Three underscored parts. This is the
[Microsoft-documented convention](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices#naming-your-tests).

**Unit tests** use the method-under-test as the first part:

```csharp
[Fact]
public void Generate_WithUppercaseRequired_IncludesAtLeastOneUppercase() { ... }

[Fact]
public void Success_DefaultArguments_ReturnsSuccessfulResultWithNullUserId() { ... }
```

**Integration tests** treat the HTTP verb + endpoint name as the "method"
(there's no single C# method under test — the endpoint is the unit):

```csharp
[Fact]
public async Task PostLogin_WithWrongPassword_Returns401WithoutToken() { ... }

[Fact]
public async Task GetEditRole_OnExistingRole_RendersRowInEditMode() { ... }
```

### Conventions

- **Every bug fix ships with a regression test.** New behaviour without a
  test is rejected.
- **Security claims need a proving test.** A comment or doc that asserts a
  security property ("validates the signature", "prevents open-redirect",
  "invalidates outstanding tokens") must be backed by a test that fails if the
  property is removed. See the [security posture](docs/security.md) for the
  per-flow threats and where each is enforced and tested. A comment claiming a
  guarantee the code does not enforce is worse than none — it makes reviewers
  trust something that is not there.
- **Tests must be order-independent.** Never depend on the side effect of
  another test. When a test mutates seeded data (rename, lockout, role
  assignment), provision a throwaway user / role with a `Guid.NewGuid()`-
  suffixed name so a re-run is safe.
- **Mocking lib is Moq.** No NSubstitute, no FakeItEasy — one library so
  test patterns stay uniform.
- **Use FluentAssertions' `because:` argument** to leave a note for the
  next reader: `body.Should().Contain("Locked", "the badge must flip
  after the lock action")`.
- **Internal API access** — adapter projects expose
  `<InternalsVisibleTo Include="VisuAuth.UnitTests" />` so the unit-test
  project can reach internal helpers (e.g. `TemporaryPasswordGenerator`)
  without leaking `public` onto the production surface.
- **Antiforgery in integration tests** — Razor Pages require a token on
  every POST. Tests GET the page first, parse the
  `__RequestVerificationToken` value with the shared regex
  `name="__RequestVerificationToken"[^>]*?value="([^"]+)"`, and include
  it in the form body.
- **Cookies in integration tests** — pass
  `WebApplicationFactoryClientOptions { HandleCookies = true }` so the
  cookie jar survives between the GET that sets the antiforgery cookie
  and the POST that consumes it.

## Local code-quality scan

A Docker Compose stack with SonarQube Community ships in the repo so you can
run the same analysis CI does (against SonarCloud) without leaving your
machine. Steps:

```powershell
docker compose -f docker-compose.sonar.yml up -d   # one-time per session
scripts/sonar-local.ps1                            # after non-trivial code changes
```

Full instructions (token bootstrap, exclusions, dashboard URL) are in
[CLAUDE.md section 10.5](CLAUDE.md). New bugs / vulnerabilities introduced
by a PR should be fixed before requesting review.

- **Check the issues list, not just the quality gate.** `INFO` / `MINOR`
  violations don't fail the gate but we keep them at zero. After a scan, query
  the issues — locally
  `curl -u "$SONAR_TOKEN:" "http://localhost:9000/api/issues/search?componentKeys=VisuAuth&inNewCodePeriod=true&resolved=false"`,
  or open the dashboard — and clear anything your change introduced before
  requesting review.

## What "drop-in" means here

The user-facing promise is:
1. Install `VisuAuth` NuGet package.
2. Add `services.AddVisuAuth()...` to `Program.cs`.
3. Add `app.MapVisuAuth()`.

Nothing else. No Node.js, no build step on the consumer side, no extra middleware to configure manually.

When in doubt, optimize for the consumer's setup, not for VisuAuth internals.

## Filing issues

Use the GitHub issue templates. Include:
- VisuAuth version
- .NET SDK version
- Minimal repro (a `dotnet new web` project that demonstrates the problem is gold)
- Expected vs actual behavior

## Pull requests

- Branch from `main` using the [naming convention](#branch-naming) above.
- **One concern per PR.** Don't refactor unrelated code.
- **PR title must follow Conventional Commits** — it becomes the squashed commit message on `main`.
- Update tests. New behavior without a test is rejected.
- Update docs/README if user-facing behavior changes.
- **If you add a VisuAuth route, link it from the sample home.** Every new
  page / endpoint must be reachable from `samples/Sample.WebApp/Program.cs`
  (the `/` landing page), so the route is manually testable and nothing ships
  invisible.
- Wait for CI to pass before requesting review.
- **Triage every review comment after opening the PR _and_ again after CI.**
  Read the conversation, the reviews, *and* the inline review comments
  (`gh api repos/VisuAuth/VisuAuth/pulls/<N>/comments`) — the automated
  reviewer leaves its findings inline, not in the main thread. Address or
  explicitly answer each one; never let an inline comment (especially a
  security finding) go unread.

## Code of conduct

Be respectful. We follow the [Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/) v2.1 — see [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) for the full text and how to report a concern privately.

## License

By contributing, you agree your contribution is licensed under the [Apache License 2.0](LICENSE).
