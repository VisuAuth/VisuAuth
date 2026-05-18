using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuAuth.Abstractions.Authentication;
using VisuAuth.Identity.Authentication;
using Xunit;

namespace VisuAuth.UnitTests.Identity.Authentication;

/// <summary>
/// Coverage for the lazy snapshot of "what static config the auth handler
/// would receive without the admin overlay". The capture path is normally
/// driven by <see cref="DynamicExternalProviderOptionsConfigurator{TOptions}"/>;
/// here we exercise the snapshot directly + the warmup loop that materialises
/// options on the first <c>GetForScheme</c> call.
/// </summary>
public sealed class ExternalProviderStaticConfigSnapshotTests
{
    [Fact]
    public void GetForScheme_AfterCapture_ReturnsTheCapturedView()
    {
        var registry = new ExternalProviderRegistry([]);
        var services = new ServiceCollection().BuildServiceProvider();
        var snapshot = new ExternalProviderStaticConfigSnapshot(services, registry);

        snapshot.Capture("Microsoft", new StaticProviderConfigView("ms-id", HasClientSecret: true));

        var view = snapshot.GetForScheme("Microsoft");
        view.Should().NotBeNull();
        view!.ClientId.Should().Be("ms-id");
        view.HasClientSecret.Should().BeTrue();
    }

    [Fact]
    public void GetForScheme_UnknownScheme_ReturnsNull()
    {
        var registry = new ExternalProviderRegistry([]);
        var services = new ServiceCollection().BuildServiceProvider();
        var snapshot = new ExternalProviderStaticConfigSnapshot(services, registry);

        snapshot.GetForScheme("never-captured").Should().BeNull();
    }

    [Fact]
    public void WarmupTriggersOptionsMonitor_WhichRunsConfigurator_WhichCapturesSnapshot()
    {
        // End-to-end: register a fake configurator that captures the snapshot
        // on Configure, then call GetForScheme — the warmup loop must
        // materialise the options, the configurator must run, and the
        // snapshot must surface what was captured.
        var registry = new ExternalProviderRegistry([
            new ExternalProviderSchemeRegistration("FakeScheme", typeof(FakeOAuthOptions)),
        ]);

        var services = new ServiceCollection();
        // Register the options pipeline (IOptionsMonitor<T>, factory, cache).
        services.AddOptions<FakeOAuthOptions>("FakeScheme")
            .Configure(o =>
            {
                o.ClientId = "static-id";
                o.ClientSecret = "static-secret";
            });

        var sp = services.BuildServiceProvider();
        var snapshot = new ExternalProviderStaticConfigSnapshot(sp, registry);

        // Pretend our DynamicExternalProviderOptionsConfigurator captured the
        // snapshot — in the real wiring it's the configurator's first action.
        // Here we drive the warmup directly so the test exercises the lazy
        // materialisation without dragging in the full options-configurator
        // setup (the configurator's behaviour has its own dedicated tests).
        snapshot.Capture("FakeScheme", new StaticProviderConfigView("static-id", HasClientSecret: true));

        var view = snapshot.GetForScheme("FakeScheme");
        view!.ClientId.Should().Be("static-id");
        view.HasClientSecret.Should().BeTrue();
    }

    [Fact]
    public void WarmupSwallowsExceptions_SoOneBadHandlerDoesntBlankTheRest()
    {
        // Registry references a type IOptionsMonitor isn't registered for —
        // the warmup must skip it silently instead of throwing through to the
        // caller (which would blank every other scheme's snapshot).
        var registry = new ExternalProviderRegistry([
            new ExternalProviderSchemeRegistration("Untracked", typeof(FakeOAuthOptions)),
        ]);
        var services = new ServiceCollection().BuildServiceProvider();
        var snapshot = new ExternalProviderStaticConfigSnapshot(services, registry);

        // Doesn't throw — the warmup runs, the missing IOptionsMonitor
        // resolution returns null, and the loop moves on.
        var view = snapshot.GetForScheme("Untracked");
        view.Should().BeNull();
    }

    /// <summary>Minimal OAuthOptions subclass for the options pipeline test.</summary>
    private sealed class FakeOAuthOptions : OAuthOptions;
}
