using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using VisuAuth.Abstractions.Users;
using VisuAuth.Entra.Configuration;

namespace VisuAuth.Entra;

/// <summary>
/// <see cref="IEmailDomainSource"/> backed by Microsoft Graph
/// <c>/domains</c>. Surfaces the tenant's <b>verified</b> domains (primary
/// first) so the admin Create-User form can offer a domain dropdown for a
/// multi-domain Entra tenant instead of a single locked suffix.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton and caches the verified-domain list for the
/// process lifetime: domains change rarely (adding a verified domain is a
/// deliberate DNS + portal operation), and the Create-User form calls this
/// on every render, so a per-request Graph round-trip would be wasteful.
/// A process restart re-reads them.
/// </para>
/// <para>
/// Best-effort: a Graph failure (e.g. the app lacks <c>Domain.Read.All</c>,
/// or a transient error) yields an empty list — the form then falls back
/// to the single configured suffix or free text rather than breaking. The
/// empty result is NOT cached, so a later render retries.
/// </para>
/// </remarks>
public sealed class EntraEmailDomainSource(IEntraGraphClient graphClient) : IEmailDomainSource, IDisposable
{
    private readonly IEntraGraphClient _graphClient =
        graphClient ?? throw new ArgumentNullException(nameof(graphClient));

    private GraphServiceClient Graph => _graphClient.GetClient();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<string>? _cached;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetEmailDomainsAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var domains = await FetchVerifiedDomainsAsync(cancellationToken);
            // Only cache a non-empty result. An empty list usually means a
            // missing permission / transient error — keep retrying on later
            // renders rather than pinning "no domains" for the whole process.
            if (domains.Count > 0)
            {
                _cached = domains;
            }
            return domains;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> FetchVerifiedDomainsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await Graph.Domains.GetAsync(rc =>
            {
                rc.QueryParameters.Select = ["id", "isVerified", "isDefault"];
            }, cancellationToken);

            return (response?.Value ?? [])
                .Where(d => d.IsVerified == true && !string.IsNullOrEmpty(d.Id))
                // Primary/default domain first, then alphabetical for a
                // stable, predictable dropdown order.
                .OrderByDescending(d => d.IsDefault == true)
                .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(d => d.Id!)
                .ToList();
        }
        catch (ODataError)
        {
            // Missing Domain.Read.All or a transient Graph error — the form
            // degrades to the single suffix / free text.
            return [];
        }
    }

    /// <summary>Releases the cache-population gate. Called once on container teardown.</summary>
    public void Dispose() => _gate.Dispose();
}
