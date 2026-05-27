using Sample.EntraExternalWebApp.Home;
using VisuAuth;
using VisuAuth.EntraExternal.DependencyInjection;

// Minimalist reference for VisuAuth + Microsoft Entra External ID.
//
// What this app demonstrates:
//   - The shortest path from a fresh ASP.NET Core project to a working
//     VisuAuth admin against a real Entra External (customer identity)
//     tenant.
//   - Zero Identity/SQLite/JWT/OAuth wire-up — Microsoft owns the user
//     directory, VisuAuth.EntraExternal reads/writes it through
//     Microsoft Graph.
//   - Capability-driven UI: the same /visuauth/admin pages the Workforce
//     sample (Sample.EntraWebApp) renders work here too, just with
//     identities[] under the hood instead of a UPN.
//
// Setup (do once):
//   1. Create an Entra tenant + app registration. Pick "External" (NOT
//      Workforce). See the README "Entra External setup" section for the
//      portal walkthrough. The app needs these Application permissions
//      on Microsoft Graph with admin consent: User.Read.All,
//      User.ReadWrite.All, AppRoleAssignment.ReadWrite.All,
//      Application.Read.All.
//   2. Populate the four required secrets:
//        cd samples/Sample.EntraExternalWebApp
//        dotnet user-secrets set "VisuAuth:EntraExternal:TenantId"     "<guid>"
//        dotnet user-secrets set "VisuAuth:EntraExternal:ClientId"     "<guid>"
//        dotnet user-secrets set "VisuAuth:EntraExternal:ClientSecret" "<value>"
//        dotnet user-secrets set "VisuAuth:EntraExternal:TenantDomain" "<tenant>.onmicrosoft.com"
//   3. dotnet run
//
// That's it. Browse http://localhost:5260 and follow the links.

var builder = WebApplication.CreateBuilder(args);

// Fluent composition (CLAUDE.md §2.1). UseAspNetIdentity / EnableMultiTenant
// are deliberately absent — the External adapter brings its own IUserStore /
// IRoleStore / IAuthenticationFlow + the no-op fallbacks for IAuditWriter /
// IJwtIssuer / ITenantContext that the EndUserUi pipeline expects.
builder.Services
    .AddVisuAuth()
    .AddAdminUi()
    .AddEndUserUi();

// The single External-specific call. Binds VisuAuth:EntraExternal:* from
// configuration, registers EntraExternalUserStore + EntraExternalRoleStore +
// EntraExternalAuthenticationFlow against a singleton GraphServiceClient
// backed by app-only (client credentials) auth.
builder.Services.AddVisuAuthEntraExternal(builder.Configuration);

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Manual-test launcher at "/" — see Sample.EntraExternalWebApp.Home.SampleEntraExternalHomePage.
// Mirrors the Workforce sample's MapSampleEntraHomePage convention so the
// inline HTML stays out of Program.cs and the route list is one place to
// update when new VisuAuth surfaces land (see memory/surface-new-urls-on-sample-home.md).
app.MapSampleEntraExternalHomePage();

app.MapVisuAuth();
app.Run();

/// <summary>
/// Marker type for WebApplicationFactory&lt;Program&gt; if integration
/// tests ever want to spin this sample up.
/// </summary>
public partial class Program;
