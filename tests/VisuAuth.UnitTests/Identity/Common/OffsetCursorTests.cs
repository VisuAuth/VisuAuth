using System.Buffers.Text;
using System.Text;
using FluentAssertions;
using VisuAuth.Identity.Common;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Common;

/// <summary>
/// The opaque offset cursor EF-backed stores hand back. Round-trips must be
/// exact, and any malformed / tampered value must decode to the first page
/// rather than throwing — a hand-edited <c>?cursor=</c> can't be allowed to
/// 500 a list endpoint.
/// </summary>
public sealed class OffsetCursorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(1_000_000)]
    public void EncodeThenDecode_RoundTripsTheOffset(int offset)
    {
        OffsetCursor.Decode(OffsetCursor.Encode(offset)).Should().Be(offset);
    }

    [Fact]
    public void Encode_ProducesUrlSafeToken()
    {
        var token = OffsetCursor.Encode(255);
        token.Should().NotContainAny("+", "/", "=", "the cursor travels in a query string, so it must be base64url");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!!")]
    [InlineData("@@@@")]
    public void Decode_NullEmptyOrMalformed_ReturnsZero(string? cursor)
    {
        OffsetCursor.Decode(cursor).Should().Be(0);
    }

    [Fact]
    public void Decode_ValidBase64ButMissingTag_ReturnsZero()
    {
        // A bare number without the format tag isn't one of our cursors.
        var untagged = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("123"));
        OffsetCursor.Decode(untagged).Should().Be(0);
    }

    [Fact]
    public void Decode_NegativeOffset_ReturnsZero()
    {
        var negative = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("o-5"));
        OffsetCursor.Decode(negative).Should().Be(0);
    }
}
