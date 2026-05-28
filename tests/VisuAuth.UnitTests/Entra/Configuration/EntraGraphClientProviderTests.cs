using FluentAssertions;
using Microsoft.Extensions.Options;
using VisuAuth.Entra.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.Entra.Configuration;

/// <summary>
/// Covers the lazy Graph-client cache: the same client is reused while the
/// credential-affecting options are unchanged, and a new one is built once they
/// change — the mechanism that makes an admin config save take effect on the
/// next Graph call without a restart.
/// </summary>
public sealed class EntraGraphClientProviderTests
{
    [Fact]
    public void GetClient_SameOptions_ReturnsCachedInstance()
    {
        var monitor = new MutableOptionsMonitor(new EntraOptions { TenantId = "t", ClientId = "c", ClientSecret = "s" });
        using var provider = new EntraGraphClientProvider(monitor);

        var first = provider.GetClient();
        var second = provider.GetClient();

        second.Should().BeSameAs(first, "unchanged credentials must not rebuild the client");
    }

    [Fact]
    public void GetClient_AfterCredentialChange_RebuildsClient()
    {
        var monitor = new MutableOptionsMonitor(new EntraOptions { TenantId = "t", ClientId = "c", ClientSecret = "s1" });
        using var provider = new EntraGraphClientProvider(monitor);

        var first = provider.GetClient();
        monitor.Set(new EntraOptions { TenantId = "t", ClientId = "c", ClientSecret = "s2" });
        var afterChange = provider.GetClient();

        afterChange.Should().NotBeSameAs(first, "a changed secret must rebuild the Graph client");
    }

    [Fact]
    public void GetClient_WhenRecomputeThrows_KeepsLastGoodClient()
    {
        var monitor = new MutableOptionsMonitor(new EntraOptions { TenantId = "t", ClientId = "c", ClientSecret = "s" });
        using var provider = new EntraGraphClientProvider(monitor);

        var first = provider.GetClient();
        // Simulate an invalid recompute (e.g. admin cleared a required field).
        monitor.ThrowOnNextRead = true;

        var afterInvalid = provider.GetClient();

        afterInvalid.Should().BeSameAs(first, "an invalid recompute must not surface as a 500 mid-request");
    }

    private sealed class MutableOptionsMonitor(EntraOptions initial) : IOptionsMonitor<EntraOptions>
    {
        private EntraOptions _value = initial;

        public bool ThrowOnNextRead { get; set; }

        public EntraOptions CurrentValue
        {
            get
            {
                if (ThrowOnNextRead)
                {
                    throw new OptionsValidationException(
                        Options.DefaultName, typeof(EntraOptions), ["TenantId is required."]);
                }
                return _value;
            }
        }

        public void Set(EntraOptions value) => _value = value;

        public EntraOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<EntraOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
