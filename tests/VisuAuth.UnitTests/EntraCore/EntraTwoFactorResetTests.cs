using System.Net;
using FluentAssertions;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Http.HttpClientLibrary;
using VisuAuth.EntraCore.Infrastructure;
using VisuAuth.UnitTests.Entra.Internal;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// Covers <see cref="EntraTwoFactorReset.RemoveAllAsync"/> — the shared
/// "delete the user's authentication methods" helper both Entra adapters
/// call. Uses the <see cref="FakeGraphHandler"/> so the polymorphic
/// methods list deserialises through real Kiota and each typed DELETE
/// goes through the production builder path.
/// </summary>
public sealed class EntraTwoFactorResetTests
{
    private const string UserId = "u-1";

    // A method of every removable type + the undeletable password method.
    private const string MethodsJson = """
        {
          "value": [
            { "@odata.type": "#microsoft.graph.microsoftAuthenticatorAuthenticationMethod", "id": "auth-1" },
            { "@odata.type": "#microsoft.graph.fido2AuthenticationMethod", "id": "fido-1" },
            { "@odata.type": "#microsoft.graph.phoneAuthenticationMethod", "id": "phone-1" },
            { "@odata.type": "#microsoft.graph.softwareOathAuthenticationMethod", "id": "oath-1" },
            { "@odata.type": "#microsoft.graph.windowsHelloForBusinessAuthenticationMethod", "id": "whfb-1" },
            { "@odata.type": "#microsoft.graph.emailAuthenticationMethod", "id": "email-1" },
            { "@odata.type": "#microsoft.graph.passwordAuthenticationMethod", "id": "pwd-1" }
          ]
        }
        """;

    [Fact]
    public async Task RemoveAllAsync_DeletesEveryRemovableMethod_SkipsPassword()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/users/u-1/authentication/methods", MethodsJson)
            .SetupDelete("/authentication/microsoftAuthenticatorMethods/auth-1")
            .SetupDelete("/authentication/fido2Methods/fido-1")
            .SetupDelete("/authentication/phoneMethods/phone-1")
            .SetupDelete("/authentication/softwareOathMethods/oath-1")
            .SetupDelete("/authentication/windowsHelloForBusinessMethods/whfb-1")
            .SetupDelete("/authentication/emailMethods/email-1");

        var deleted = await EntraTwoFactorReset.RemoveAllAsync(BuildGraph(handler), UserId);

        deleted.Should().Be(6, "all six removable methods are deleted; the password method is left in place");
        // No DELETE was attempted against a passwordMethods endpoint.
        // (Materialise the paths first — NotContain's predicate is an
        // expression tree, which can't host the null-conditional operator.)
        var deletePaths = handler.RecordedRequests
            .Where(r => r.Method == HttpMethod.Delete)
            .Select(r => r.RequestUri?.AbsolutePath ?? string.Empty)
            .ToList();
        deletePaths.Should().NotContain(p => p.Contains("passwordMethods", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveAllAsync_NoMethods_ReturnsZero_NoDeletes()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/users/u-1/authentication/methods", """{ "value": [] }""");

        var deleted = await EntraTwoFactorReset.RemoveAllAsync(BuildGraph(handler), UserId);

        deleted.Should().Be(0);
        handler.RecordedRequests.Should().ContainSingle("only the list call should have been made");
    }

    [Fact]
    public async Task RemoveAllAsync_OnlyPassword_ReturnsZero_NothingDeleted()
    {
        var handler = new FakeGraphHandler()
            .SetupGet("/users/u-1/authentication/methods", """
                { "value": [ { "@odata.type": "#microsoft.graph.passwordAuthenticationMethod", "id": "pwd-1" } ] }
                """);

        var deleted = await EntraTwoFactorReset.RemoveAllAsync(BuildGraph(handler), UserId);

        deleted.Should().Be(0, "the password method is never deleted — it's the account credential");
    }

    [Fact]
    public async Task RemoveAllAsync_ListReturns404_PropagatesODataError()
    {
        // The caller (store) translates this to "user not found"; the
        // helper just lets it bubble.
        var handler = new FakeGraphHandler()
            .SetupError(HttpMethod.Get, "/users/u-1/authentication/methods", HttpStatusCode.NotFound,
                "Request_ResourceNotFound", "Resource does not exist.");

        var act = () => EntraTwoFactorReset.RemoveAllAsync(BuildGraph(handler), UserId);

        await act.Should().ThrowAsync<ODataError>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAllAsync_BlankUserId_Throws(string? id)
    {
        var act = () => EntraTwoFactorReset.RemoveAllAsync(BuildGraph(new FakeGraphHandler()), id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveAllAsync_NullGraph_Throws()
    {
        var act = () => EntraTwoFactorReset.RemoveAllAsync(null!, UserId);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("graph");
    }

    private static GraphServiceClient BuildGraph(FakeGraphHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthProvider(), httpClient: httpClient);
        return new GraphServiceClient(adapter);
    }
}
