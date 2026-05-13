using Microsoft.EntityFrameworkCore;
using VisuAuth.Abstractions.Tenancy;
using VisuAuth.Identity.MultiTenancy;

namespace Sample.WebApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
    : MultiTenantIdentityDbContext<ApplicationUser>(options, tenantContext)
{
}
