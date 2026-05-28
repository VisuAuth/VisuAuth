using FluentAssertions;
using VisuAuth.EntraCore.Infrastructure;
using Xunit;

namespace VisuAuth.UnitTests.EntraCore;

/// <summary>
/// The Graph continuation-link cursor codec. The security-critical behaviour is
/// in <see cref="GraphPageCursor.TryDecode"/>: it must round-trip a same-origin
/// HTTPS link but refuse anything else, so a tampered cursor can never redirect
/// a bearer-token-bearing request off the Graph endpoint (SSRF / token leak).
/// </summary>
public sealed class GraphPageCursorTests
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    [Fact]
    public void Encode_NullOrEmpty_ReturnsNull()
    {
        GraphPageCursor.Encode(null).Should().BeNull();
        GraphPageCursor.Encode(string.Empty).Should().BeNull();
    }

    [Fact]
    public void EncodeThenDecode_SameOriginAndResource_RoundTripsTheLink()
    {
        const string nextLink = "https://graph.microsoft.com/v1.0/users?$skiptoken=ABC123";
        var cursor = GraphPageCursor.Encode(nextLink);

        cursor.Should().NotBeNull();
        GraphPageCursor.TryDecode(cursor, GraphBase, "users", out var decoded).Should().BeTrue();
        decoded.Should().Be(nextLink);
    }

    [Theory]
    [InlineData("https://evil.example.com/v1.0/users?$skiptoken=ABC")]   // different host
    [InlineData("http://graph.microsoft.com/v1.0/users?$skiptoken=ABC")]  // not HTTPS
    [InlineData("https://graph.microsoft.com:8443/v1.0/users?$skiptoken=ABC")] // different port
    public void TryDecode_OffOriginOrInsecureLink_ReturnsFalse(string nextLink)
    {
        var cursor = GraphPageCursor.Encode(nextLink);

        GraphPageCursor.TryDecode(cursor, GraphBase, "users", out var decoded).Should().BeFalse(
            "a cursor that doesn't resolve to the configured Graph origin must never be followed");
        decoded.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/groups?$skiptoken=ABC")]      // different collection
    [InlineData("https://graph.microsoft.com/beta/users?$skiptoken=ABC")]       // different base path
    [InlineData("https://graph.microsoft.com/v1.0/usersExtra?$skiptoken=ABC")]  // prefix-but-not-segment
    public void TryDecode_SameOriginButWrongEndpoint_ReturnsFalse(string nextLink)
    {
        // Origin alone isn't enough — a same-origin cursor pointed at another
        // Graph endpoint would still travel with the app bearer token.
        var cursor = GraphPageCursor.Encode(nextLink);

        GraphPageCursor.TryDecode(cursor, GraphBase, "users", out var decoded).Should().BeFalse(
            "the cursor path must be pinned to the configured base path + expected collection");
        decoded.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-valid-base64-%%%")]
    public void TryDecode_NullEmptyOrMalformed_ReturnsFalse(string? cursor)
    {
        GraphPageCursor.TryDecode(cursor, GraphBase, "users", out var decoded).Should().BeFalse();
        decoded.Should().BeEmpty();
    }
}
