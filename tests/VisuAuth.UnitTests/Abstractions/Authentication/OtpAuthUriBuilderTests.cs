using FluentAssertions;
using VisuAuth.Abstractions.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Abstractions.Authentication;

/// <summary>
/// Unit coverage for the pure helper that builds the otpauth:// URI shown
/// to authenticator apps and normalises user-typed codes.
/// </summary>
public sealed class OtpAuthUriBuilderTests
{
    [Fact]
    public void Build_WithCanonicalInputs_ReturnsRfcKeyUriShape()
    {
        var uri = OtpAuthUriBuilder.Build(
            issuer: "VisuAuth",
            accountName: "alice@example.com",
            secretBase32: "JBSWY3DPEHPK3PXP");

        uri.Should().StartWith("otpauth://totp/VisuAuth:alice%40example.com");
        uri.Should().Contain("secret=JBSWY3DPEHPK3PXP");
        uri.Should().Contain("issuer=VisuAuth");
        uri.Should().Contain("algorithm=SHA1");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
    }

    [Fact]
    public void Build_WithIssuerContainingSpaces_PercentEncodesIssuerInPathAndQuery()
    {
        var uri = OtpAuthUriBuilder.Build(
            issuer: "Acme Corp",
            accountName: "bob@example.com",
            secretBase32: "ABCDEFGH");

        uri.Should().Contain("Acme%20Corp:bob%40example.com");
        uri.Should().Contain("issuer=Acme%20Corp");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithBlankIssuer_Throws(string? issuer)
    {
        var act = () => OtpAuthUriBuilder.Build(issuer!, "alice", "ABC");

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(issuer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithBlankAccountName_Throws(string? accountName)
    {
        var act = () => OtpAuthUriBuilder.Build("VisuAuth", accountName!, "ABC");

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(accountName));
    }

    [Fact]
    public void FormatForManualEntry_With20CharSecret_GroupsBy4WithSpaces()
    {
        var formatted = OtpAuthUriBuilder.FormatForManualEntry("JBSWY3DPEHPK3PXPMNOP");

        formatted.Should().Be("JBSW Y3DP EHPK 3PXP MNOP");
    }

    [Fact]
    public void FormatForManualEntry_PreservesUppercaseAndStripsExistingSpaces()
    {
        var formatted = OtpAuthUriBuilder.FormatForManualEntry(" jbsw y3dp ehpk 3pxp ");

        formatted.Should().Be("JBSW Y3DP EHPK 3PXP");
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("123_456", "123456")]
    [InlineData("  abc 123  ", "abc123")]
    public void Normalize_StripsWhitespaceAndSeparators(string input, string expected)
    {
        OtpAuthUriBuilder.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("- -_")]
    public void Normalize_WithNoCharactersOfInterest_ReturnsNull(string? input)
    {
        OtpAuthUriBuilder.Normalize(input).Should().BeNull();
    }
}
