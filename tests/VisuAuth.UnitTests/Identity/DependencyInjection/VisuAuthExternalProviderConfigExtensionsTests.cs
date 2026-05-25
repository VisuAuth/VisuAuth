using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Identity.Authentication;
using VisuAuth.Identity.DependencyInjection;
using Xunit;

namespace VisuAuth.UnitTests.Identity.DependencyInjection;

/// <summary>
/// Regression tests for the DI wiring of the dynamic external-provider
/// options layer. The original 0.2.0-alpha registered the configurator under
/// <see cref="IConfigureNamedOptions{TOptions}"/> only, which made
/// <see cref="OptionsFactory{TOptions}"/> skip it silently (its ctor pulls
/// <see cref="IEnumerable{T}"/> of <see cref="IConfigureOptions{TOptions}"/>,
/// not the named-options interface). End-effect: admin edits never reached
/// the runtime OAuth handler and the static-snapshot side channel was
/// always empty. These tests pin down the contract so that bug can't sneak
/// back in.
/// </summary>
public sealed class VisuAuthExternalProviderConfigExtensionsTests
{
    [Fact]
    public void AddVisuAuthDynamicExternalProviderOptions_RegistersConfiguratorUnderIConfigureOptions()
    {
        // The OptionsFactory<TOptions> ctor resolves IEnumerable<IConfigureOptions<TOptions>>.
        // Our configurator MUST be reachable through that enumerable, or the
        // overlay never runs at runtime.
        var services = new ServiceCollection();
        services.AddOptions<FakeOAuthOptions>("FakeScheme");
        services.AddVisuAuthDynamicExternalProviderOptions<FakeOAuthOptions>("FakeScheme");

        using var sp = services.BuildServiceProvider();
        var configures = sp.GetServices<IConfigureOptions<FakeOAuthOptions>>().ToArray();

        configures.Should().Contain(c => c is DynamicExternalProviderOptionsConfigurator<FakeOAuthOptions>,
            "the configurator must appear in IConfigureOptions<TOptions> so OptionsFactory picks it up");
    }

    [Fact]
    public void DynamicConfigurator_AppliedByOptionsFactory_ReceivesConfigureCalls()
    {
        // End-to-end: build the DI graph the way the consumer would,
        // resolve OptionsFactory<TOptions>, call Create(scheme), and verify
        // that BOTH the consumer's static lambda AND our overlay ran (proved
        // by the snapshot getting populated with the static value).
        var services = new ServiceCollection();
        services.AddOptions<FakeOAuthOptions>("FakeScheme").Configure(o =>
        {
            o.ClientId = "static-from-consumer";
            o.ClientSecret = "static-secret";
        });
        services.AddVisuAuthDynamicExternalProviderOptions<FakeOAuthOptions>("FakeScheme");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IOptionsFactory<FakeOAuthOptions>>();
        _ = factory.Create("FakeScheme");

        // Snapshot must hold what was on options *before* our overlay ran —
        // exactly the consumer's static value, which proves the configurator
        // executed inside the factory pipeline.
        var snapshot = sp.GetRequiredService<ExternalProviderStaticConfigSnapshot>();
        var view = snapshot.GetForScheme("FakeScheme");
        view.Should().NotBeNull();
        view!.ClientId.Should().Be("static-from-consumer");
        view.HasClientSecret.Should().BeTrue();
    }

    [Fact]
    public void DynamicConfigurator_OnUnrelatedScheme_DoesNotTouchOptions()
    {
        // The configurator was registered for "OurScheme" — calling Create
        // for "OtherScheme" with the same TOptions must NOT touch the
        // options (the name-guard at the top of Configure stops us). This
        // is the contract that lets multiple providers share TOptions
        // without stepping on each other.
        var services = new ServiceCollection();
        services.AddOptions<FakeOAuthOptions>("OtherScheme").Configure(o =>
        {
            o.ClientId = "untouched";
            o.ClientSecret = "untouched-secret";
        });
        services.AddVisuAuthDynamicExternalProviderOptions<FakeOAuthOptions>("OurScheme");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IOptionsFactory<FakeOAuthOptions>>();
        var result = factory.Create("OtherScheme");

        result.ClientId.Should().Be("untouched", "the unrelated configurator must skip via the name-guard");
        result.ClientSecret.Should().Be("untouched-secret");

        // The snapshot is keyed by scheme — "OtherScheme" wasn't captured
        // by our configurator (we don't manage it), so GetForScheme returns
        // null. (We don't assert about OurScheme here: the lazy warmup
        // path materialises OurScheme's options and the configurator
        // captures whatever it sees, which is empty in this test setup —
        // that's a separate concern with its own dedicated tests.)
        var snapshot = sp.GetRequiredService<ExternalProviderStaticConfigSnapshot>();
        snapshot.GetForScheme("OtherScheme").Should().BeNull();
    }

    [Fact]
    public void DynamicConfigurator_DropsSentinel_WhenNeitherStaticNorDbSuppliedCredentials()
    {
        // No consumer .Configure, no DB row — both ClientId and Secret start
        // empty. The configurator's EnsureValidatable step has to drop a
        // non-empty placeholder so OAuthOptions.Validate (called by the
        // factory pipeline) doesn't throw. Without this fallback the very
        // first IOptionsMonitor.Get on the scheme crashes the request.
        var services = new ServiceCollection();
        services.AddOptions<FakeOAuthOptions>("FakeScheme");
        services.AddVisuAuthDynamicExternalProviderOptions<FakeOAuthOptions>("FakeScheme");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IOptionsFactory<FakeOAuthOptions>>();
        var result = factory.Create("FakeScheme");

        result.ClientId.Should().NotBeNullOrEmpty("Validate would throw on empty ClientId");
        result.ClientSecret.Should().NotBeNullOrEmpty("Validate would throw on empty ClientSecret");
        result.ClientId.Should().Be(DynamicExternalProviderOptionsConfigurator<FakeOAuthOptions>.NotConfiguredSentinel);
        result.ClientSecret.Should().Be(DynamicExternalProviderOptionsConfigurator<FakeOAuthOptions>.NotConfiguredSentinel);
    }

    [Fact]
    public void DynamicConfigurator_SentinelDoesNotPolluteSnapshot()
    {
        // The fallback is applied AFTER CaptureStaticSnapshot — the snapshot
        // must reflect the genuine "nothing was here" state so the admin UI
        // doesn't show a misleading "from code" badge for providers nobody
        // actually configured. The fact that we later patched options.ClientId
        // for Validate's sake is an internal implementation detail.
        var services = new ServiceCollection();
        services.AddOptions<FakeOAuthOptions>("FakeScheme");
        services.AddVisuAuthDynamicExternalProviderOptions<FakeOAuthOptions>("FakeScheme");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IOptionsFactory<FakeOAuthOptions>>();
        _ = factory.Create("FakeScheme");

        var snapshot = sp.GetRequiredService<ExternalProviderStaticConfigSnapshot>();
        var view = snapshot.GetForScheme("FakeScheme");
        view.Should().NotBeNull();
        view!.ClientId.Should().BeNull("nothing supplied the ClientId statically — the sentinel is internal");
        view.HasClientSecret.Should().BeFalse();
    }

    /// <summary>Minimal OAuthOptions subclass used as the generic argument in tests.</summary>
    private sealed class FakeOAuthOptions : OAuthOptions;
}
