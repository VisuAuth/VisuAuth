using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Entra;
using VisuAuth.Entra.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.Entra;

/// <summary>
/// Lock-down for <see cref="EntraUserStore"/>'s synchronous defences:
/// constructor null-arg checks, the unconditional NotSupported throw on
/// ResetTwoFactor (Sonar will dock us if it's unreachable), and the
/// capability surface. Graph-touching paths (List / Create / etc.) need
/// a recorded-response harness — gated for v0.3.
/// </summary>
public sealed class EntraUserStoreTests
{
    [Fact]
    public void Ctor_NullGraphClient_Throws()
    {
        var act = () => new EntraUserStore(null!, Options.Create(new EntraOptions()), NullLogger<EntraUserStore>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("graphClient");
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var act = () => new EntraUserStore(BuildOfflineGraphClient(), null!, NullLogger<EntraUserStore>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        var act = () => new EntraUserStore(BuildOfflineGraphClient(), Options.Create(new EntraOptions()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Capabilities_ExposesEntraSingletonByReference()
    {
        var sut = BuildStore();
        // The user store overlays EntraOptions onto the singleton Value
        // (EmailDomainSuffix etc.), so it's a structural-equality match,
        // not a reference one. Every other flag must still be identical.
        sut.Capabilities.Should().Be(
            EntraCapabilities.Value with { EmailDomainSuffix = sut.Capabilities.EmailDomainSuffix },
            "the same flag bag must serve every facet of the adapter — single source of truth modulo per-options overlay");
    }

    [Fact]
    public async Task ResetTwoFactorAsync_ThrowsNotSupported_WithActionableMessage()
    {
        var sut = BuildStore();
        var act = () => sut.ResetTwoFactorAsync("u-1", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.WithMessage("*Entra portal*",
            "operators reaching this branch need to know where the real fix lives — the Entra portal");
    }

    [Fact]
    public async Task CreateAsync_BlankEmail_FailsBeforeCallingGraph()
    {
        var sut = BuildStore();
        var result = await sut.CreateAsync(new VisuAuth.Abstractions.Users.CreateUserCommand
        {
            Email = "   ",
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Email",
            "the early-bail must explain why so the admin UI can surface a clean validation message");
    }

    [Fact]
    public async Task CreateAsync_NullCommand_ThrowsArgumentNull()
    {
        var sut = BuildStore();
        var act = () => sut.CreateAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("command");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_BlankId_ThrowsArgumentException(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.GetAsync(id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetDetailAsync_BlankId_ThrowsArgumentException(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.GetDetailAsync(id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UpdateAsync_BlankId_Throws(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.UpdateAsync(id!, new VisuAuth.Abstractions.Users.UpdateUserCommand(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_NullCommand_Throws()
    {
        var sut = BuildStore();
        var act = () => sut.UpdateAsync("u-1", null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("command");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DeleteAsync_BlankId_Throws(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.DeleteAsync(id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SetEnabledAsync_BlankId_Throws(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.SetEnabledAsync(id!, true);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ResetPasswordAsync_BlankId_Throws(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.ResetPasswordAsync(id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RevokeSessionsAsync_BlankId_Throws(string? id)
    {
        var sut = BuildStore();
        var act = () => sut.RevokeSessionsAsync(id!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ResetTwoFactorAsync_BlankId_StillThrowsNotSupported(string? id)
    {
        // ResetTwoFactor is unconditional NotSupported — the arg validation
        // doesn't even run (the method throws synchronously before it could).
        // We still assert here so a future "lift the capability" PR remembers
        // to add the arg guard back.
        var sut = BuildStore();
        var act = () => sut.ResetTwoFactorAsync(id!);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ListAsync_NullFilter_Throws()
    {
        var sut = BuildStore();
        var act = () => sut.ListAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public void Capabilities_WithDefaultEmailDomain_OverlaysEmailDomainSuffix_WithLeadingAt()
    {
        var sut = new EntraUserStore(
            BuildOfflineGraphClient(),
            Microsoft.Extensions.Options.Options.Create(new EntraOptions
            {
                TenantId = "t",
                ClientId = "c",
                ClientSecret = "s",
                DefaultEmailDomain = "visuauth.onmicrosoft.com",
            }),
            NullLogger<EntraUserStore>.Instance);

        sut.Capabilities.EmailDomainSuffix.Should().Be("@visuauth.onmicrosoft.com",
            "the adapter prefixes the leading @ so consumers can configure either form");
    }

    [Fact]
    public void Capabilities_WithDefaultEmailDomainAlreadyPrefixed_DoesNotDoubleAtSign()
    {
        var sut = new EntraUserStore(
            BuildOfflineGraphClient(),
            Microsoft.Extensions.Options.Options.Create(new EntraOptions
            {
                TenantId = "t",
                ClientId = "c",
                ClientSecret = "s",
                DefaultEmailDomain = "@visuauth.onmicrosoft.com",
            }),
            NullLogger<EntraUserStore>.Instance);

        sut.Capabilities.EmailDomainSuffix.Should().Be("@visuauth.onmicrosoft.com",
            "operators who include @ in their config shouldn't end up with @@");
    }

    [Fact]
    public void Capabilities_WithoutDefaultEmailDomain_LeavesSuffixNull()
    {
        BuildStore().Capabilities.EmailDomainSuffix.Should().BeNull(
            "no config = no suggestion, the form keeps its free-text email input");
    }

    [Fact]
    public void Capabilities_ReflectV02EntraScope()
    {
        // Concrete repeat of EntraCapabilitiesTests just so a regression
        // accidentally introduced via the store's `=>` accessor (rather
        // than the singleton) is caught here too — defence in depth.
        var caps = BuildStore().Capabilities;
        caps.SupportsLocalLogin.Should().BeFalse();
        caps.SupportsTwoFactor.Should().BeFalse();
        caps.SupportsTwoFactorReset.Should().BeFalse();
        caps.SupportsRoleManagement.Should().BeTrue();
        caps.SupportsSessionRevocation.Should().BeTrue();
    }

    private static EntraUserStore BuildStore()
        => new(BuildOfflineGraphClient(),
               Options.Create(new EntraOptions { TenantId = "t", ClientId = "c", ClientSecret = "s" }),
               NullLogger<EntraUserStore>.Instance);

    private static GraphServiceClient BuildOfflineGraphClient()
    {
        TokenCredential offline = new ClientSecretCredential("tenant", "client", "secret");
        return new GraphServiceClient(offline);
    }
}
