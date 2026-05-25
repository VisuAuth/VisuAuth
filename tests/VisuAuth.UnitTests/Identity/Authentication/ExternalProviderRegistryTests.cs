using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OAuth;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Tests for the singleton registry that exposes which schemes the host
/// wired through <c>AddVisuAuthDynamicExternalProviderOptions&lt;TOptions&gt;</c>.
/// The admin page hits this on every render — the lookup must stay O(1) on
/// the scheme name and respect insertion order so the UI lists providers in
/// the order the consumer registered them.
/// </summary>
public sealed class ExternalProviderRegistryTests
{
    [Fact]
    public void Empty_RegistrationsIsEmpty_AndNothingIsRegistered()
    {
        var registry = new ExternalProviderRegistry([]);

        registry.Registrations.Should().BeEmpty();
        registry.IsRegistered("Microsoft").Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_ReportsTrueForWiredSchemeAndFalseForOthers()
    {
        var registry = new ExternalProviderRegistry([
            new ExternalProviderSchemeRegistration("Microsoft", typeof(OAuthOptions)),
            new ExternalProviderSchemeRegistration("Google", typeof(OAuthOptions)),
        ]);

        registry.IsRegistered("Microsoft").Should().BeTrue();
        registry.IsRegistered("Google").Should().BeTrue();
        registry.IsRegistered("Facebook").Should().BeFalse();
    }

    [Fact]
    public void Registrations_PreservesRegistrationOrder()
    {
        // Order matters: the admin page renders Active providers in this order,
        // not alphabetical, so it matches the consumer's Program.cs ordering.
        var registry = new ExternalProviderRegistry([
            new ExternalProviderSchemeRegistration("Zeta", typeof(OAuthOptions)),
            new ExternalProviderSchemeRegistration("Alpha", typeof(OAuthOptions)),
            new ExternalProviderSchemeRegistration("Mike", typeof(OAuthOptions)),
        ]);

        registry.Registrations.Select(r => r.Scheme).Should().Equal("Zeta", "Alpha", "Mike");
    }

    [Fact]
    public void IsRegistered_IsCaseSensitive_BecauseAuthSchemeNamesAre()
    {
        // ASP.NET Core authentication scheme lookup is case-sensitive
        // (StringComparer.Ordinal in IAuthenticationSchemeProvider) — the
        // registry has to match so a "google" lookup doesn't accidentally hit
        // a "Google" wiring.
        var registry = new ExternalProviderRegistry([
            new ExternalProviderSchemeRegistration("Google", typeof(OAuthOptions)),
        ]);

        registry.IsRegistered("Google").Should().BeTrue();
        registry.IsRegistered("google").Should().BeFalse();
    }
}
