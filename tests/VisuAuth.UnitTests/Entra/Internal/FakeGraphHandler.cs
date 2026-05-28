using System.Net;
using System.Text;
using Microsoft.Kiota.Abstractions.Authentication;

namespace VisuAuth.UnitTests.Entra.Internal;

/// <summary>
/// Test double for the <see cref="HttpMessageHandler"/> the
/// <see cref="Microsoft.Graph.GraphServiceClient"/> talks through. Each
/// registered route returns canned JSON + the status code we want — the
/// store-under-test exercises real serialisation, real Kiota deserialisation,
/// and real ODataError unwrapping, without ever touching the network.
/// </summary>
/// <remarks>
/// <para>
/// Routes are matched by HTTP method + a substring on the path so the
/// fixture can be loose ("any /users PATCH" without re-specifying $select).
/// Unmatched requests fail loud — the test author sees exactly what call
/// the SUT made that we didn't expect.
/// </para>
/// <para>
/// All Graph endpoints we mock here are version-pinned to <c>v1.0</c>
/// (the EntraOptions default); the handler doesn't reconstruct request
/// URIs, so swapping cloud / beta is a runtime concern not a fixture
/// concern.
/// </para>
/// </remarks>
internal sealed class FakeGraphHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = [];

    public List<HttpRequestMessage> RecordedRequests { get; } = [];

    /// <summary>
    /// Request bodies captured at send time, index-aligned with
    /// <see cref="RecordedRequests"/>. Buffered DURING SendAsync because
    /// HttpClient disposes the request content once the call returns —
    /// reading <c>request.Content</c> afterwards yields nothing. Null for
    /// bodyless requests (GET / DELETE). Lets a test assert on the exact
    /// JSON a store / sync serialised onto the wire.
    /// </summary>
    public List<string?> RecordedRequestBodies { get; } = [];

    public FakeGraphHandler Setup(HttpMethod method, string pathSubstring, HttpStatusCode status, string body = "")
    {
        _routes.Add(new Route(method, pathSubstring, status, body));
        return this;
    }

    public FakeGraphHandler SetupGet(string path, string jsonBody)
        => Setup(HttpMethod.Get, path, HttpStatusCode.OK, jsonBody);

    public FakeGraphHandler SetupPostNoContent(string path)
        => Setup(HttpMethod.Post, path, HttpStatusCode.NoContent);

    public FakeGraphHandler SetupPostJson(string path, string jsonBody, HttpStatusCode status = HttpStatusCode.Created)
        => Setup(HttpMethod.Post, path, status, jsonBody);

    public FakeGraphHandler SetupPatch(string path, HttpStatusCode status = HttpStatusCode.NoContent)
        => Setup(HttpMethod.Patch, path, status);

    public FakeGraphHandler SetupDelete(string path)
        => Setup(HttpMethod.Delete, path, HttpStatusCode.NoContent);

    public FakeGraphHandler SetupError(HttpMethod method, string path, HttpStatusCode status, string errorCode, string message)
    {
        // Plain interpolation (concatenation actually) — keeps the
        // analyser happy without raw-string brace gymnastics for the
        // nested ODataError envelope.
        var body = "{\"error\":{\"code\":\"" + errorCode + "\",\"message\":\"" + message + "\"}}";
        return Setup(method, path, status, body);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RecordedRequests.Add(request);
        // Buffer the body now — the content stream is disposed once this
        // returns, so a later test read would come back empty.
        RecordedRequestBodies.Add(
            request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
        var path = request.RequestUri?.PathAndQuery ?? string.Empty;
        var match = _routes.FirstOrDefault(r =>
            r.Method == request.Method
            && path.Contains(r.PathSubstring, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            // Loud failure helps the test author see exactly which call
            // they forgot to wire — a silent 404 would mask the issue
            // behind an ODataError downstream.
            throw new InvalidOperationException(
                $"FakeGraphHandler: no route matches {request.Method} {path}. "
                + $"Registered: {string.Join("; ", _routes.Select(r => $"{r.Method} *{r.PathSubstring}*"))}");
        }

        var response = new HttpResponseMessage(match.Status);
        if (!string.IsNullOrEmpty(match.Body))
        {
            response.Content = new StringContent(match.Body, Encoding.UTF8, "application/json");
        }
        return Task.FromResult(response);
    }

    private sealed record Route(HttpMethod Method, string PathSubstring, HttpStatusCode Status, string Body);
}

/// <summary>
/// Auth provider that injects a static fake bearer token into every Graph
/// request. The fake handler doesn't validate auth; this just keeps Kiota's
/// pipeline happy.
/// </summary>
internal sealed class AnonymousAuthProvider : IAuthenticationProvider
{
    public Task AuthenticateRequestAsync(
        Microsoft.Kiota.Abstractions.RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        request.Headers.TryAdd("Authorization", "Bearer fake-token-for-tests");
        return Task.CompletedTask;
    }
}
