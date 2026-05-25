using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Abstractions.Common;
using VisuAuth.AdminUi.ExternalProviders;

namespace VisuAuth.AdminUi.Pages.Admin.ExternalProviders;

/// <summary>
/// Admin page for editing OAuth external-provider credentials at runtime
/// (mounts at <c>/visuauth/admin/external-providers</c>). The visible rows
/// are an OUTER JOIN between three sources:
/// <list type="bullet">
///   <item><see cref="IExternalProviderRegistry"/> — schemes the host wired
///     through <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>
///     (truth about what's runnable).</item>
///   <item><see cref="KnownProviderCatalog"/> — built-in list of ~20 popular
///     providers so the admin discovers what's possible.</item>
///   <item><see cref="IExternalProviderConfigStore"/> — saved credentials.</item>
/// </list>
/// The resulting buckets are <c>Active</c> (wired + known, editable),
/// <c>Custom</c> (wired but unknown to the catalogue, editable),
/// <c>Available</c> (known but not wired — ghost cards with "How to
/// activate" snippet), and <c>Orphan</c> (DB row for a scheme the host no
/// longer wires — warning + Delete button).
/// </summary>
public sealed class IndexModel(
    IExternalProviderConfigStore configStore,
    IExternalProviderRegistry registry,
    IExternalProviderOptionsCacheInvalidator cacheInvalidator,
    IExternalProviderStaticConfigSnapshot staticSnapshot,
    IAuditWriter auditWriter,
    IStringLocalizer<AdminSharedResources> localizer) : PageModel
{
    // Shared localizer key — used by every handler that needs to bail out
    // on a missing scheme parameter. Extracted so the literal lives in one
    // place (csharpsquid:S1192).
    private const string MissingSchemeKey = "ExternalProviders.Error.MissingScheme";

    private readonly IExternalProviderConfigStore _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    private readonly IExternalProviderRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IExternalProviderOptionsCacheInvalidator _cacheInvalidator = cacheInvalidator ?? throw new ArgumentNullException(nameof(cacheInvalidator));
    private readonly IExternalProviderStaticConfigSnapshot _staticSnapshot = staticSnapshot ?? throw new ArgumentNullException(nameof(staticSnapshot));
    private readonly IAuditWriter _audit = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly IStringLocalizer<AdminSharedResources> _l = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [BindProperty]
    public EditForm EditFields { get; set; } = new();

    /// <summary>Wired schemes that match a catalogue entry — edit-capable rows.</summary>
    public IReadOnlyList<ProviderRow> ActiveProviders { get; private set; } = [];

    /// <summary>Wired schemes that DON'T match any catalogue entry — edit-capable, generic icon.</summary>
    public IReadOnlyList<ProviderRow> CustomProviders { get; private set; } = [];

    /// <summary>Catalogue entries the host hasn't wired yet — ghost cards with install snippet.</summary>
    public IReadOnlyList<KnownExternalProvider> AvailableProviders { get; private set; } = [];

    /// <summary>DB rows for schemes the host stopped wiring — warning, Delete only.</summary>
    public IReadOnlyList<ProviderRow> OrphanProviders { get; private set; } = [];

    /// <summary>Scheme whose row is currently in edit mode (renders the form inline).</summary>
    public string? EditingScheme { get; private set; }

    public string? ActionMessage { get; private set; }

    public IReadOnlyList<string> ActionErrors { get; private set; } = [];

    /// <summary>Convenience for the partial — sum of active+custom (everything actually configurable).</summary>
    public int ConfigurableCount => ActiveProviders.Count + CustomProviders.Count;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    public async Task<IActionResult> OnGetEditAsync(string? scheme, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            ActionErrors = [_l[MissingSchemeKey].Value];
        }
        else if (!_registry.IsRegistered(scheme))
        {
            // Edit is meaningless for un-wired schemes — the save would succeed
            // but the login page still wouldn't show the button. Surface as an
            // explicit error rather than letting the admin save into the void.
            ActionErrors = [_l["ExternalProviders.Error.SchemeNotRegistered", scheme].Value];
        }
        else
        {
            EditingScheme = scheme;
        }
        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? scheme, CancellationToken cancellationToken)
    {
        var guard = await GuardSaveAsync(scheme, cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var existing = await _configStore.GetAsync(scheme!, tenantId: null, cancellationToken);
        var catalogue = KnownProviderCatalog.Find(scheme!);
        var displayName = catalogue?.DisplayName ?? existing?.DisplayName ?? scheme!;
        var secretToSave = ResolveSecretToSave();

        var result = await _configStore.SaveAsync(new SaveExternalProviderConfigCommand
        {
            Scheme = scheme!,
            DisplayName = displayName,
            TenantId = null,
            ClientId = string.IsNullOrWhiteSpace(EditFields.ClientId) ? null : EditFields.ClientId.Trim(),
            PlainTextClientSecret = secretToSave,
            IsEnabled = EditFields.IsEnabled,
        }, cancellationToken);

        await ApplySaveOutcomeAsync(scheme!, displayName, secretToSave, result, cancellationToken);

        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    /// <summary>
    /// Early-bail checks for the save handler. Returns the IActionResult
    /// to render, or null when the input is fit to hit the store.
    /// </summary>
    private async Task<IActionResult?> GuardSaveAsync(string? scheme, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            ActionErrors = [_l[MissingSchemeKey].Value];
            await LoadAsync(cancellationToken);
            return RenderResult();
        }
        if (!_registry.IsRegistered(scheme))
        {
            ActionErrors = [_l["ExternalProviders.Error.SchemeNotRegistered", scheme].Value];
            await LoadAsync(cancellationToken);
            return RenderResult();
        }
        return null;
    }

    /// <summary>
    /// Encryption rule used by Save:
    /// — non-empty plaintext  → encrypt and replace.
    /// — empty + Clear ticked → drop the stored secret entirely.
    /// — empty + no Clear     → preserve whatever ciphertext was stored.
    /// </summary>
    private string? ResolveSecretToSave()
    {
        var rawSecret = EditFields.ClientSecret;
        if (!string.IsNullOrEmpty(rawSecret))
        {
            return rawSecret;
        }
        if (EditFields.ClearClientSecret)
        {
            return string.Empty;
        }
        return null;
    }

    /// <summary>
    /// Mutates page state (errors / message / EditingScheme) AND emits the
    /// matching audit event. Kept out of OnPostSaveAsync so that method
    /// stays under the cognitive-complexity budget.
    /// </summary>
    private async Task ApplySaveOutcomeAsync(
        string scheme,
        string displayName,
        string? secretToSave,
        UserResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            EditingScheme = scheme;
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["ExternalProviders.Error.SaveFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.ExternalProviderSaved,
                TargetType = AuditTargetTypes.ExternalProvider,
                TargetId = scheme,
                TargetLabel = displayName,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);
            return;
        }

        _cacheInvalidator.Invalidate(scheme);
        ActionMessage = _l["ExternalProviders.Action.Saved", scheme].Value;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.ExternalProviderSaved,
            TargetType = AuditTargetTypes.ExternalProvider,
            TargetId = scheme,
            TargetLabel = displayName,
            Outcome = AuditOutcome.Success,
            Payload = new Dictionary<string, string?>
            {
                // Never log the secret itself — just whether one was provided.
                ["clientIdSet"] = !string.IsNullOrWhiteSpace(EditFields.ClientId) ? "true" : "false",
                ["clientSecretChanged"] = secretToSave is null ? "false" : "true",
                ["isEnabled"] = EditFields.IsEnabled ? "true" : "false",
            },
        }, cancellationToken);

        EditFields = new EditForm();
    }

    public Task<IActionResult> OnPostToggleEnabledAsync(string? scheme, bool isEnabled, CancellationToken cancellationToken)
        => ToggleAsync(scheme, isEnabled, cancellationToken);

    public async Task<IActionResult> OnPostBulkEnableAsync(CancellationToken cancellationToken)
    {
        await BulkSetEnabledAsync(true, cancellationToken);
        ActionMessage = _l["ExternalProviders.Action.BulkEnabled"].Value;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.ExternalProviderBulkEnabled,
            TargetType = AuditTargetTypes.ExternalProvider,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    public async Task<IActionResult> OnPostBulkDisableAsync(CancellationToken cancellationToken)
    {
        await BulkSetEnabledAsync(false, cancellationToken);
        ActionMessage = _l["ExternalProviders.Action.BulkDisabled"].Value;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.ExternalProviderBulkDisabled,
            TargetType = AuditTargetTypes.ExternalProvider,
            Outcome = AuditOutcome.Success,
        }, cancellationToken);

        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    /// <summary>
    /// Removes an orphan DB row whose scheme is no longer registered. The
    /// invalidator still gets called even though the row was orphan — cheap
    /// no-op when the cache has nothing to drop, and catches the edge case
    /// where the row was re-wired during the same admin session.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteOrphanAsync(string? scheme, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            ActionErrors = [_l[MissingSchemeKey].Value];
            await LoadAsync(cancellationToken);
            return RenderResult();
        }
        var result = await _configStore.DeleteAsync(scheme, tenantId: null, cancellationToken);
        if (!result.IsSuccess)
        {
            ActionErrors = [result.Error ?? _l["ExternalProviders.Error.DeleteFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.ExternalProviderOrphanDeleted,
                TargetType = AuditTargetTypes.ExternalProvider,
                TargetId = scheme,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error,
            }, cancellationToken);
        }
        else
        {
            _cacheInvalidator.Invalidate(scheme);
            ActionMessage = _l["ExternalProviders.Action.OrphanDeleted", scheme].Value;

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.ExternalProviderOrphanDeleted,
                TargetType = AuditTargetTypes.ExternalProvider,
                TargetId = scheme,
                Outcome = AuditOutcome.Success,
            }, cancellationToken);
        }
        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    private async Task<IActionResult> ToggleAsync(string? scheme, bool isEnabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            ActionErrors = [_l[MissingSchemeKey].Value];
            await LoadAsync(cancellationToken);
            return RenderResult();
        }
        var result = await _configStore.SetEnabledAsync(scheme, tenantId: null, isEnabled, cancellationToken);
        var toggleAction = isEnabled
            ? AuditActions.ExternalProviderEnabled
            : AuditActions.ExternalProviderDisabled;
        if (!result.IsSuccess)
        {
            ActionErrors = [result.Error ?? _l["ExternalProviders.Error.SaveFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = toggleAction,
                TargetType = AuditTargetTypes.ExternalProvider,
                TargetId = scheme,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error,
            }, cancellationToken);
        }
        else
        {
            _cacheInvalidator.Invalidate(scheme);
            ActionMessage = isEnabled
                ? _l["ExternalProviders.Action.Enabled", scheme].Value
                : _l["ExternalProviders.Action.Disabled", scheme].Value;

            await _audit.WriteAsync(new AuditEvent
            {
                Action = toggleAction,
                TargetType = AuditTargetTypes.ExternalProvider,
                TargetId = scheme,
                Outcome = AuditOutcome.Success,
            }, cancellationToken);
        }
        await LoadAsync(cancellationToken);
        return RenderResult();
    }

    /// <summary>
    /// Bulk-flips only configurable rows (Active + Custom). Orphans are
    /// excluded because they're not addressable from the runtime layer
    /// anyway, and a flipped orphan would silently do nothing on /login.
    /// </summary>
    private async Task BulkSetEnabledAsync(bool isEnabled, CancellationToken cancellationToken)
    {
        var all = await _configStore.ListAsync(tenantId: null, cancellationToken);
        foreach (var row in all)
        {
            if (!_registry.IsRegistered(row.Scheme) || row.IsEnabled == isEnabled)
            {
                continue;
            }
            var result = await _configStore.SetEnabledAsync(row.Scheme, tenantId: null, isEnabled, cancellationToken);
            if (result.IsSuccess)
            {
                _cacheInvalidator.Invalidate(row.Scheme);
            }
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var dbRows = await _configStore.ListAsync(tenantId: null, cancellationToken);
        var dbBySchema = dbRows.ToDictionary(r => r.Scheme, StringComparer.Ordinal);
        var registeredSchemes = _registry.Registrations
            .Select(r => r.Scheme)
            .ToHashSet(StringComparer.Ordinal);

        // One pass over the registry → projects each scheme into a row,
        // then split into active / custom by whether the catalogue knows
        // the scheme. ToLookup keeps the original registration order
        // within each bucket, which is what the UI wants (matches
        // Program.cs ordering).
        var buckets = _registry.Registrations
            .Select(reg =>
            {
                dbBySchema.TryGetValue(reg.Scheme, out var view);
                var cat = KnownProviderCatalog.Find(reg.Scheme);
                var staticView = _staticSnapshot.GetForScheme(reg.Scheme);
                return new ProviderRow(
                    reg.Scheme,
                    cat?.DisplayName ?? view?.DisplayName ?? reg.Scheme,
                    view,
                    cat,
                    staticView);
            })
            .ToLookup(r => r.Catalogue is not null);

        ActiveProviders = [.. buckets[true]];
        CustomProviders = [.. buckets[false]];

        AvailableProviders = [.. KnownProviderCatalog.All
            .Where(p => !registeredSchemes.Contains(p.Scheme))];

        // Orphan rows can never have a static snapshot (no registered
        // configurator captured them) — pass null so the partial doesn't
        // try to render a "from code" badge by mistake.
        OrphanProviders = [.. dbBySchema.Values
            .Where(v => !registeredSchemes.Contains(v.Scheme))
            .Select(v => new ProviderRow(
                v.Scheme,
                v.DisplayName,
                v,
                KnownProviderCatalog.Find(v.Scheme),
                StaticConfig: null))];
    }

    private IActionResult RenderResult()
        => Request.Headers.ContainsKey("HX-Request") ? Partial("_ProvidersTable", this) : Page();

    /// <summary>
    /// View-model shape merging the four input sources into one row the
    /// partial can render uniformly. <see cref="Config"/> is null when the
    /// scheme is wired but has no DB row yet (first-time admin visit);
    /// <see cref="Catalogue"/> is null for custom providers;
    /// <see cref="StaticConfig"/> is null for orphan rows and for schemes
    /// whose options haven't been materialised yet (the snapshot warmup is
    /// best-effort).
    /// </summary>
    public sealed record ProviderRow(
        string Scheme,
        string DisplayName,
        ExternalProviderConfigView? Config,
        KnownExternalProvider? Catalogue,
        StaticProviderConfigView? StaticConfig);

    /// <summary>Form fields posted from the inline edit row.</summary>
    public sealed class EditForm
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>True when the admin ticked "Clear stored secret" — we then save an empty cipher.</summary>
        public bool ClearClientSecret { get; set; }
    }
}
