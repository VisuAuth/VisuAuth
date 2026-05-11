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

Requires the **.NET 10 SDK** (10.0.203 or later).

## Project layout

- `src/VisuAuth.Abstractions/` — Public contracts (`IUserStore`, `IRoleStore`, `UserBackendCapabilities`, etc.). Stable from v0.1.
- `src/VisuAuth.Identity/` — ASP.NET Core Identity adapter.
- `src/VisuAuth.AdminUi/` — Admin dashboard (Razor Pages + htmx).
- `src/VisuAuth.EndUserUi/` — Login, register, password reset, and other end-user pages.
- `src/VisuAuth/` — Meta-package depending on the others.
- `samples/Sample.WebApp/` — Reference consumer app.

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


- **Nullable reference types** are enabled and required.
- **Warnings as errors** in `Release` builds.
- **File-scoped namespaces** always.
- **`sealed` by default** unless inheritance is intentional.
- **`async`/`await` always with `CancellationToken`** for any I/O.
- **`TimeProvider`** instead of `DateTime.Now`/`DateTime.UtcNow`.
- **Records for DTOs**, classes for behavior.
- 1 file = 1 public type.

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
- Wait for CI to pass before requesting review.

## Code of conduct

Be respectful. We follow the [Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/).

## License

By contributing, you agree your contribution is licensed under the [Apache License 2.0](LICENSE).
