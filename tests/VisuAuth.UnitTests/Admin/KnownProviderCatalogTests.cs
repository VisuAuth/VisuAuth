using FluentAssertions;
using VisuAuth.AdminUi.ExternalProviders;
using Xunit;

namespace VisuAuth.UnitTests.Admin;

/// <summary>
/// Sanity tests for the built-in catalogue of OAuth providers the admin UI
/// surfaces. These don't validate the upstream NuGet packages exist (that
/// happens implicitly when a consumer copy-pastes the install snippet); they
/// just lock in the catalogue's shape so accidental edits (typos, swapped
/// fields, missing docs URLs) get caught at PR time.
/// </summary>
public sealed class KnownProviderCatalogTests
{
    [Fact]
    public void All_ContainsTheExpectedTwentyProviders()
    {
        // Spot-check a stable subset rather than the full 20 — adding a 21st
        // entry shouldn't break the test, only changes to the contract or
        // removal of a flagship provider should.
        var schemes = KnownProviderCatalog.All.Select(p => p.Scheme).ToHashSet();
        schemes.Should().Contain([
            "Microsoft", "Google", "Apple", "GitHub", "Facebook",
            "LinkedIn", "X", "Discord", "Slack", "Twitch",
            "Spotify", "GitLab", "Reddit", "Amazon", "Salesforce",
        ]);
        KnownProviderCatalog.All.Should().HaveCountGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void All_EveryEntryHasNonEmptyMandatoryFields()
    {
        foreach (var p in KnownProviderCatalog.All)
        {
            p.Scheme.Should().NotBeNullOrWhiteSpace();
            p.DisplayName.Should().NotBeNullOrWhiteSpace();
            p.NuGetPackageId.Should().NotBeNullOrWhiteSpace($"{p.Scheme} needs a package id for the install snippet");
            p.OptionsTypeName.Should().EndWith("Options",
                $"{p.Scheme} options type follows the *Options convention");
            p.AddExtensionMethod.Should().StartWith("Add",
                $"{p.Scheme} extension method follows the AddXxx convention");
        }
    }

    [Fact]
    public void All_DocsUrlsAreHttpsLinksWhenPresent()
    {
        foreach (var p in KnownProviderCatalog.All.Where(p => p.DocsUrl is not null))
        {
            p.DocsUrl!.Should().StartWith("https://",
                $"{p.Scheme} docs URL must use https so the admin's outbound click isn't downgraded");
        }
    }

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("microsoft")]   // case-insensitive
    [InlineData("DISCORD")]
    public void Find_KnownScheme_ReturnsEntry(string scheme)
        => KnownProviderCatalog.Find(scheme).Should().NotBeNull();

    [Fact]
    public void Find_UnknownScheme_ReturnsNull()
        => KnownProviderCatalog.Find("not-a-real-provider").Should().BeNull();
}
