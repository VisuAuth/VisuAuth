using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VisuAuth.Abstractions.Auditing;
using VisuAuth.Abstractions.Configuration;

namespace VisuAuth.AdminUi.Pages.Admin.EntraConfig;

/// <summary>
/// Admin page for editing a backend adapter's settings at runtime (mounts at
/// <c>/visuauth/admin/entra-config</c>). Renders one section per registered
/// <see cref="IAdapterConfigSchema"/>, showing each setting's current value
/// with "From DB" / "From code" source badges, and persists overrides through
/// <see cref="IAdapterConfigStore"/> (secrets encrypted at rest).
/// </summary>
/// <remarks>
/// The store + schemas are only present when the consumer opts in
/// (<c>AddVisuAuthAdapterConfigStore()</c> + an adapter's <c>...DbConfig()</c>),
/// so they're injected as optional: without them the page renders a
/// "not enabled" explainer (<see cref="ConfigAvailable"/>) and the sidebar link
/// stays hidden. After a successful save the matching
/// <see cref="IAdapterConfigChangeNotifier"/> fires so the change takes effect
/// without an app restart. Secret plaintext is never echoed back to the page
/// nor written to the audit log.
/// </remarks>
public sealed class IndexModel(
    IStringLocalizer<AdminSharedResources> localizer,
    IAuditWriter auditWriter,
    IEnumerable<IAdapterConfigSchema> schemas,
    IEnumerable<IAdapterConfigChangeNotifier> notifiers,
    IAdapterConfigStore? store = null) : PageModel
{
    private readonly IStringLocalizer<AdminSharedResources> _l =
        localizer ?? throw new ArgumentNullException(nameof(localizer));
    private readonly IAuditWriter _audit =
        auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    private readonly List<IAdapterConfigSchema> _schemas =
        (schemas ?? throw new ArgumentNullException(nameof(schemas))).ToList();
    private readonly List<IAdapterConfigChangeNotifier> _notifiers =
        (notifiers ?? throw new ArgumentNullException(nameof(notifiers))).ToList();
    private readonly IAdapterConfigStore? _store = store;

    /// <summary>True when both a store and at least one adapter schema are wired.</summary>
    public bool ConfigAvailable => _store is not null && _schemas.Count > 0;

    /// <summary>One renderable section per adapter schema.</summary>
    public IReadOnlyList<AdapterConfigSection> Sections { get; private set; } = [];

    /// <summary>Adapter whose section should open in edit mode after a post.</summary>
    public string? EditingAdapter { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? ActionMessage { get; private set; }

    /// <summary>Posted key→value map (form names <c>FieldValues[Key]</c>).</summary>
    [BindProperty]
    public Dictionary<string, string> FieldValues { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Secret keys the admin ticked "clear" for.</summary>
    [BindProperty]
    public List<string> ClearSecrets { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(string? adapter, CancellationToken cancellationToken)
    {
        if (_store is null || string.IsNullOrWhiteSpace(adapter))
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var schema = _schemas.FirstOrDefault(s => string.Equals(s.Adapter, adapter, StringComparison.Ordinal));
        if (schema is null)
        {
            Errors = [_l["EntraConfig.Error.UnknownAdapter"].Value];
            await LoadAsync(cancellationToken);
            return Page();
        }

        var (values, changeFlags) = BuildValues(schema);
        var result = await _store.SaveAsync(
            new SaveAdapterConfigCommand { Adapter = adapter, Values = values },
            cancellationToken);

        if (!result.IsSuccess)
        {
            EditingAdapter = adapter;
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? _l["EntraConfig.Error.SaveFailed"].Value];

            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditActions.AdapterConfigSaved,
                TargetType = AuditTargetTypes.AdapterConfig,
                TargetId = adapter,
                TargetLabel = schema.DisplayName,
                Outcome = AuditOutcome.Failure,
                FailureReason = result.Error ?? string.Join("; ", result.ValidationErrors),
            }, cancellationToken);

            await LoadAsync(cancellationToken);
            return Page();
        }

        // Take effect without a restart: nudge the adapter to recompute options.
        foreach (var notifier in _notifiers.Where(n => string.Equals(n.Adapter, adapter, StringComparison.Ordinal)))
        {
            notifier.NotifyChanged();
        }

        ActionMessage = _l["EntraConfig.Action.Saved", schema.DisplayName].Value;

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditActions.AdapterConfigSaved,
            TargetType = AuditTargetTypes.AdapterConfig,
            TargetId = adapter,
            TargetLabel = schema.DisplayName,
            Outcome = AuditOutcome.Success,
            // Per-key change flags only — never the values, secret or not.
            Payload = changeFlags,
        }, cancellationToken);

        FieldValues.Clear();
        ClearSecrets.Clear();
        await LoadAsync(cancellationToken);
        return Page();
    }

    private (List<AdapterConfigValue> Values, Dictionary<string, string?> ChangeFlags) BuildValues(IAdapterConfigSchema schema)
    {
        var values = new List<AdapterConfigValue>(schema.Fields.Count);
        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            FieldValues.TryGetValue(field.Key, out var posted);
            string? value;

            if (field.IsSecret)
            {
                if (ClearSecrets.Contains(field.Key))
                {
                    value = string.Empty; // clear
                }
                else if (string.IsNullOrEmpty(posted))
                {
                    value = null; // blank secret = preserve existing
                }
                else
                {
                    value = posted; // set new secret
                }
            }
            else
            {
                // Non-secret text input always posts; blank means "clear the
                // override and fall back to the code value".
                value = posted ?? string.Empty;
            }

            values.Add(new AdapterConfigValue { Key = field.Key, IsSecret = field.IsSecret, Value = value });
            flags[field.Key] = ChangeFlag(value);
        }

        return (values, flags);
    }

    // Audit-safe change descriptor — never the value itself.
    private static string ChangeFlag(string? value) => value switch
    {
        null => "unchanged",
        "" => "cleared",
        _ => "set",
    };

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_store is null)
        {
            Sections = [];
            return;
        }

        var sections = new List<AdapterConfigSection>(_schemas.Count);
        foreach (var schema in _schemas.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var stored = await _store.ListAsync(schema.Adapter, cancellationToken);
            var byKey = stored.ToDictionary(e => e.Key, StringComparer.Ordinal);

            var fields = schema.Fields.Select(f =>
            {
                byKey.TryGetValue(f.Key, out var dbEntry);
                return new AdapterConfigFieldView
                {
                    Field = f,
                    HasDbValue = dbEntry?.HasValue ?? false,
                    DbValue = f.IsSecret ? null : dbEntry?.Value,
                    HasCodeValue = schema.HasCodeValue(f.Key),
                    CodeValue = f.IsSecret ? null : schema.GetCodeValue(f.Key),
                };
            }).ToList();

            sections.Add(new AdapterConfigSection
            {
                Adapter = schema.Adapter,
                DisplayName = schema.DisplayName,
                Fields = fields,
            });
        }
        Sections = sections;
    }

    /// <summary>Renderable section for one adapter.</summary>
    public sealed class AdapterConfigSection
    {
        public required string Adapter { get; init; }
        public required string DisplayName { get; init; }
        public required IReadOnlyList<AdapterConfigFieldView> Fields { get; init; }
    }

    /// <summary>Renderable state for one setting: schema + effective sources.</summary>
    public sealed class AdapterConfigFieldView
    {
        public required AdapterConfigField Field { get; init; }
        public bool HasDbValue { get; init; }
        public string? DbValue { get; init; }
        public bool HasCodeValue { get; init; }
        public string? CodeValue { get; init; }

        /// <summary>Value to pre-fill a non-secret input with (DB wins, else code).</summary>
        public string? EffectiveValue => DbValue ?? CodeValue;
    }
}
