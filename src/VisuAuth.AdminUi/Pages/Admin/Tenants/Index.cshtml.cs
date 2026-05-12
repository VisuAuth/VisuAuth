using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using VisuAuth.Abstractions.Tenancy;

namespace VisuAuth.AdminUi.Pages.Admin.Tenants;

/// <summary>
/// Tenants catalogue page. Lists every tenant, supports inline create / rename /
/// delete (mirroring the roles catalogue), and hosts the <c>Switch</c> handler
/// that the sidebar dropdown POSTs to.
/// </summary>
public sealed class IndexModel(
    ITenantStore tenantStore,
    ITenantContext tenantContext,
    IOptions<TenantOptions> tenantOptions) : PageModel
{
    private readonly ITenantStore _tenantStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
    private readonly ITenantContext _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    private readonly TenantOptions _options = tenantOptions?.Value ?? throw new ArgumentNullException(nameof(tenantOptions));

    [BindProperty]
    public string? NewTenantId { get; set; }

    [BindProperty]
    public string? NewTenantDisplayName { get; set; }

    [BindProperty]
    public string? RenamedDisplayName { get; set; }

    public IReadOnlyList<TenantSummary> Tenants { get; private set; } = [];

    public string? CurrentTenantId => _tenantContext.CurrentTenantId;

    public string? ActionMessage { get; private set; }

    public IReadOnlyList<string> ActionErrors { get; private set; } = [];

    /// <summary>When set, the row with this id renders inline as a rename form.</summary>
    public string? EditingTenantId { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Partial("_TenantsCatalogue", this);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var result = await _tenantStore.CreateAsync(
            NewTenantId?.Trim() ?? string.Empty,
            NewTenantDisplayName?.Trim(),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Failed to create tenant."];
        }
        else
        {
            ActionMessage = $"Tenant '{NewTenantId?.Trim()}' created.";
            NewTenantId = null;
            NewTenantDisplayName = null;
        }

        await LoadAsync(cancellationToken);
        return Partial("_TenantsCatalogue", this);
    }

    public async Task<IActionResult> OnGetEditTenantAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            ActionErrors = ["Missing tenant id."];
        }
        else
        {
            EditingTenantId = id;
        }
        await LoadAsync(cancellationToken);
        return Partial("_TenantsCatalogue", this);
    }

    public async Task<IActionResult> OnPostRenameAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            ActionErrors = ["Missing tenant id."];
            await LoadAsync(cancellationToken);
            return Partial("_TenantsCatalogue", this);
        }

        var trimmed = RenamedDisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            EditingTenantId = id;
            ActionErrors = ["Display name is required."];
            await LoadAsync(cancellationToken);
            return Partial("_TenantsCatalogue", this);
        }

        var result = await _tenantStore.RenameAsync(id, trimmed, cancellationToken);
        if (!result.IsSuccess)
        {
            EditingTenantId = id;
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Failed to rename tenant."];
        }
        else
        {
            ActionMessage = $"Tenant '{id}' renamed.";
            RenamedDisplayName = null;
        }

        await LoadAsync(cancellationToken);
        return Partial("_TenantsCatalogue", this);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            ActionErrors = ["Missing tenant id."];
            await LoadAsync(cancellationToken);
            return Partial("_TenantsCatalogue", this);
        }

        var result = await _tenantStore.DeleteAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Failed to delete tenant."];
        }
        else
        {
            ActionMessage = $"Tenant '{id}' deleted.";
        }

        await LoadAsync(cancellationToken);
        return Partial("_TenantsCatalogue", this);
    }

    /// <summary>
    /// Switches the scope cookie. Called from the sidebar dropdown. Sends the
    /// admin back to <c>returnUrl</c> so they stay where they were when they
    /// switched.
    /// </summary>
    public IActionResult OnPostSwitch(string? tenantId, string? returnUrl)
    {
        var safeReturn = SanitiseReturnUrl(returnUrl);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Path = "/",
            // Persist across the browser session so the admin does not have to
            // re-pick the tenant on every reload. One year is generous; admins
            // can clear it via the "(global)" option.
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        };

        var trimmed = tenantId?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            Response.Cookies.Delete(_options.CookieName);
        }
        else
        {
            Response.Cookies.Append(_options.CookieName, trimmed, cookieOptions);
        }

        return Redirect(safeReturn);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Tenants = await _tenantStore.ListAsync(cancellationToken);
    }

    private string SanitiseReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/visuauth/admin/users";
        }

        // Only accept local URLs — prevents open-redirect abuse via a crafted
        // sidebar form submission.
        return Url.IsLocalUrl(returnUrl) ? returnUrl : "/visuauth/admin/users";
    }
}
