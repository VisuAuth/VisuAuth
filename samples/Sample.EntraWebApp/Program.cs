using Sample.EntraWebApp.Home;
using VisuAuth;
using VisuAuth.Entra.DependencyInjection;
using VisuAuth.Entra.Web.DependencyInjection;
using VisuAuth.EntraCore.DependencyInjection;

// Minimalist reference for VisuAuth + Microsoft Entra ID.
//
// What this app demonstrates:
//   - The shortest path from a fresh ASP.NET Core project to a working
//     VisuAuth admin against a real Entra tenant.
//   - Zero Identity/SQLite/JWT/OAuth wire-up — Microsoft owns the user
//     directory, VisuAuth.Entra reads/writes it through Microsoft Graph.
//   - Capability-driven UI: the same /visuauth/admin pages that
//     Sample.WebApp renders against Identity work against Graph here,
//     just with the Locked / 2FA / PendingEmail tiles auto-hidden and
//     the login form swapped for "Sign in with Microsoft".
//
// Setup (do once):
//   1. Create an Entra tenant + app registration (Workforce). See the
//      README "Entra adapter" section for portal steps. The app needs
//      these Application permissions on Microsoft Graph with admin
//      consent: User.Read.All, User.ReadWrite.All,
//      AppRoleAssignment.ReadWrite.All, Application.Read.All.
//   2. Populate the three required secrets:
//        cd samples/Sample.EntraWebApp
//        dotnet user-secrets set "VisuAuth:Entra:TenantId"     "<guid>"
//        dotnet user-secrets set "VisuAuth:Entra:ClientId"     "<guid>"
//        dotnet user-secrets set "VisuAuth:Entra:ClientSecret" "<value>"
//   3. dotnet run
//
// That's it. Browse http://localhost:5240 and follow the links.

var builder = WebApplication.CreateBuilder(args);

// Fluent composition (CLAUDE.md §2.1). UseAspNetIdentity / EnableMultiTenant
// are deliberately absent — the Entra adapter brings its own IUserStore /
// IRoleStore / IAuthenticationFlow + the no-op fallbacks for IAuditWriter /
// IJwtIssuer / ITenantContext that the EndUserUi pipeline expects.
builder.Services
    .AddVisuAuth()
    .AddAdminUi()
    .AddEndUserUi();

// The single Entra-specific call. Binds VisuAuth:Entra:* from configuration,
// registers EntraUserStore + EntraRoleStore + EntraAuthenticationFlow against
// a singleton GraphServiceClient backed by app-only (client credentials) auth.
builder.Services.AddVisuAuthEntra(builder.Configuration);

// Operator sign-in. AddVisuAuthEntra above authenticates the *app* to Graph;
// it does not sign a *human* in and registers no authentication scheme. The
// admin dashboard requires an authenticated user, so without this call it has
// nothing to challenge with and nobody can get in.
//
// Needs a SECOND app registration (the sign-in one, with a redirect URI) —
// separate from the Graph app above. See VisuAuth.Entra.Web/README.md:
//   dotnet user-secrets set "VisuAuth:Entra:Web:TenantId"     "<guid>"
//   dotnet user-secrets set "VisuAuth:Entra:Web:ClientId"     "<guid>"
//   dotnet user-secrets set "VisuAuth:Entra:Web:ClientSecret" "<value>"
//
// To restrict the dashboard to an app role rather than "any user in the
// tenant", register a policy under
// VisuAuthAdminUiServiceCollectionExtensions.AdminAuthorizationPolicy with
// RequireRole(...).
builder.Services.AddVisuAuthEntraSignIn(builder.Configuration);

// Opt-in: surface the tenant's Entra audit events — sign-ins AND directory
// changes (user CRUD, role assignments) — on /visuauth/admin/audit-log, and
// feed the dashboard "logins per day" chart. Needs Microsoft Graph
// AuditLog.Read.All (admin-consented) + an Entra ID P1 licence; degrades to
// an empty audit view without them. Drop this line to keep the "not enabled"
// hint on the audit page.
builder.Services.AddVisuAuthEntraAuditLog();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Manual-test launcher at "/" — see Sample.EntraWebApp.Home.SampleEntraHomePage.
// Mirrors Sample.WebApp's MapSampleHomePage convention so the inline HTML
// stays out of Program.cs and the route list is one place to update when
// new VisuAuth surfaces land.
app.MapSampleEntraHomePage();

app.MapVisuAuth();
app.Run();

/// <summary>
/// Marker type for WebApplicationFactory&lt;Program&gt; if integration
/// tests ever want to spin this sample up.
/// </summary>
public partial class Program;
