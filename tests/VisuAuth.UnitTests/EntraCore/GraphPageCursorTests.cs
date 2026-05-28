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
    public void EncodeThenDecode_SameOrigin_RoundTripsTheLink()
    {
        const string nextLink = "https://graph.microsoft.com/v1.0/users?$skiptoken=ABC123";
        var cursor = GraphPageCursor.Encode(nextLink);

        cursor.Should().NotBeNull();
        GraphPageCursor.TryDecode(cursor, GraphBase, out var decoded).Should().BeTrue();
        decoded.Should().Be(nextLink);
    }

    [Theory]
    [InlineData("https://evil.example.com/v1.0/users?$skiptoken=ABC")]   // different host
    [InlineData("http://graph.microsoft.com/v1.0/users?$skiptoken=ABC")]  // not HTTPS
    [InlineData("https://graph.microsoft.com:8443/v1.0/users?$skiptoken=ABC")] // different port
    public void TryDecode_OffOriginOrInsecureLink_ReturnsFalse(string nextLink)
    {
        var cursor = GraphPageCursor.Encode(nextLink);

        GraphPageCursor.TryDecode(cursor, GraphBase, out var decoded).Should().BeFalse(
            "a cursor that doesn't resolve to the configured Graph origin must never be followed");
        decoded.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-valid-base64-%%%")]
    public void TryDecode_NullEmptyOrMalformed_ReturnsFalse(string? cursor)
    {
        GraphPageCursor.TryDecode(cursor, GraphBase, out var decoded).Should().BeFalse();
        decoded.Should().BeEmpty();
    }
}
