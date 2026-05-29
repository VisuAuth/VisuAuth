using Microsoft.Graph;

namespace VisuAuth.Entra.Configuration;

/// <summary>
/// Supplies the current <see cref="GraphServiceClient"/> to the Entra stores.
/// Resolving the client through this seam (rather than injecting
/// <see cref="GraphServiceClient"/> directly) keeps its lifetime owned by the
/// singleton provider: consumers never hold a DI-tracked instance that would be
/// disposed at scope end, and each call observes the latest client after a
/// config change.
/// </summary>
public interface IEntraGraphClient
{
    /// <summary>The current Graph client (rebuilt lazily when options change).</summary>
    GraphServiceClient GetClient();
}
