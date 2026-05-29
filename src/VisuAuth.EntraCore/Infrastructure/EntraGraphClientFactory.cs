using Azure.Identity;
using Microsoft.Graph;

namespace VisuAuth.EntraCore.Infrastructure;

/// <summary>
/// Builds the singleton <see cref="GraphServiceClient"/> the Entra
/// adapter family uses to talk to Microsoft Graph. Centralises the
/// app-only (client credentials) auth + scope wiring so each adapter's
/// DI extension just calls <see cref="Create"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>GraphServiceClient</c> is documented as thread-safe in Graph SDK
/// 5.x and re-uses one <see cref="HttpClient"/> internally, so callers
/// register it as a singleton. The wrapped
/// <see cref="ClientSecretCredential"/> caches tokens for their full
/// lifetime — the cost of construction is paid once per process.
/// </para>
/// <para>
/// Hardcoded scope <c>https://graph.microsoft.com/.default</c> is the
/// right answer for app-only flows: it asks for every permission the
/// registered app already has admin consent for. Per-call narrowing
/// doesn't apply to the client-credentials grant.
/// </para>
/// </remarks>
public static class EntraGraphClientFactory
{
    private static readonly string[] DefaultScopes =
        { "https://graph.microsoft.com/.default" };

    /// <summary>
    /// Constructs a <see cref="GraphServiceClient"/> against the
    /// supplied tenant / app credentials.
    /// </summary>
    /// <param name="tenantId">Directory (tenant) GUID.</param>
    /// <param name="clientId">Application (client) GUID.</param>
    /// <param name="clientSecret">Client secret value.</param>
    /// <param name="graphBaseUrl">
    /// Optional Graph endpoint base URL (e.g. a sovereign cloud). When supplied
    /// it overrides the SDK default so requests target the right cloud; null /
    /// empty keeps the SDK's public-cloud default.
    /// </param>
    public static GraphServiceClient Create(string tenantId, string clientId, string clientSecret, string? graphBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var client = new GraphServiceClient(credential, DefaultScopes);
        if (!string.IsNullOrWhiteSpace(graphBaseUrl))
        {
            // Point the request adapter at the configured cloud; otherwise the
            // GraphBaseUrl option would be ignored for the actual transport.
            client.RequestAdapter.BaseUrl = graphBaseUrl;
        }
        return client;
    }
}
