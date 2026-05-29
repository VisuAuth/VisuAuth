using Microsoft.Graph;
using VisuAuth.Entra.Configuration;

namespace VisuAuth.UnitTests.Entra.Internal;

/// <summary>
/// Test <see cref="IEntraGraphClient"/> that hands back a fixed
/// <see cref="GraphServiceClient"/> (typically one wired to a
/// <see cref="FakeGraphHandler"/>), so the stores can be exercised without the
/// real provider / DI lifetime.
/// </summary>
internal sealed class StubEntraGraphClient(GraphServiceClient client) : IEntraGraphClient
{
    private readonly GraphServiceClient _client = client;

    public GraphServiceClient GetClient() => _client;
}

internal static class StubEntraGraphClientExtensions
{
    /// <summary>Wraps a raw client as an <see cref="IEntraGraphClient"/> for store ctors.</summary>
    public static IEntraGraphClient AsEntraGraphClient(this GraphServiceClient client) => new StubEntraGraphClient(client);
}
