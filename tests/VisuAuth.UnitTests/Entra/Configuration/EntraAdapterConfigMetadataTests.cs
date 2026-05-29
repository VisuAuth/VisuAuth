using FluentAssertions;
using Microsoft.Extensions.Options;
using VisuAuth.Entra.Configuration;
using Xunit;

namespace VisuAuth.UnitTests.Entra.Configuration;

/// <summary>
/// Covers the small metadata pieces of the Entra DB-config feature: the schema
/// the admin page renders from, the static snapshot it reads "From code"
/// presence from, and the change signal / notifier / token source that make a
/// save take effect without a restart.
/// </summary>
public sealed class EntraAdapterConfigMetadataTests
{
    [Fact]
    public void Schema_CoversEveryEntraKey_AndMarksClientSecretSecret()
    {
        var schema = new EntraAdapterConfigSchema(new EntraConfigStaticSnapshot());

        schema.Adapter.Should().Be(EntraAdapterConfigKeys.Adapter);
        schema.DisplayName.Should().NotBeNullOrWhiteSpace();
        schema.Fields.Select(f => f.Key).Should().BeEquivalentTo(new[]
        {
            EntraAdapterConfigKeys.TenantId,
            EntraAdapterConfigKeys.ClientId,
            EntraAdapterConfigKeys.ClientSecret,
            EntraAdapterConfigKeys.AppRoleResourceId,
            EntraAdapterConfigKeys.GraphBaseUrl,
            EntraAdapterConfigKeys.DefaultEmailDomain,
        });
        schema.Fields.Single(f => f.Key == EntraAdapterConfigKeys.ClientSecret).IsSecret.Should().BeTrue();
        schema.Fields.Single(f => f.Key == EntraAdapterConfigKeys.TenantId).IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Schema_HasCodeValueAndGetCodeValue_DelegateToSnapshot()
    {
        var snapshot = new EntraConfigStaticSnapshot();
        snapshot.CaptureOnce(new EntraOptions { TenantId = "code-tenant", ClientId = "c", ClientSecret = "s" });
        var schema = new EntraAdapterConfigSchema(snapshot);

        schema.HasCodeValue(EntraAdapterConfigKeys.TenantId).Should().BeTrue();
        schema.GetCodeValue(EntraAdapterConfigKeys.TenantId).Should().Be("code-tenant");
        schema.HasCodeValue(EntraAdapterConfigKeys.ClientSecret).Should().BeTrue();
        schema.GetCodeValue(EntraAdapterConfigKeys.ClientSecret).Should().BeNull("secret values are never surfaced");
        schema.HasCodeValue(EntraAdapterConfigKeys.DefaultEmailDomain).Should().BeFalse("not set in code");
    }

    [Fact]
    public void Snapshot_CaptureOnce_RecordsNonSecret_AndSecretPresenceOnly()
    {
        var snapshot = new EntraConfigStaticSnapshot();
        snapshot.CaptureOnce(new EntraOptions
        {
            TenantId = "t",
            ClientId = "c",
            ClientSecret = "s",
            GraphBaseUrl = "https://graph.microsoft.com/v1.0",
        });

        snapshot.GetValue(EntraAdapterConfigKeys.GraphBaseUrl).Should().Be("https://graph.microsoft.com/v1.0");
        snapshot.HasValue(EntraAdapterConfigKeys.ClientSecret).Should().BeTrue();
        snapshot.GetValue(EntraAdapterConfigKeys.ClientSecret).Should().BeNull();
        snapshot.HasValue(EntraAdapterConfigKeys.DefaultEmailDomain).Should().BeFalse();
    }

    [Fact]
    public void Snapshot_CaptureOnce_IsIdempotent()
    {
        var snapshot = new EntraConfigStaticSnapshot();
        snapshot.CaptureOnce(new EntraOptions { TenantId = "first", ClientId = "c", ClientSecret = "s" });
        snapshot.CaptureOnce(new EntraOptions { TenantId = "second", ClientId = "c", ClientSecret = "s" });

        snapshot.GetValue(EntraAdapterConfigKeys.TenantId).Should().Be("first", "the first capture wins");
    }

    [Fact]
    public void ChangeSignal_Notify_FiresPreviousToken_AndIssuesAFreshOne()
    {
        var signal = new EntraConfigChangeSignal();
        var token = signal.GetChangeToken();

        token.HasChanged.Should().BeFalse();
        signal.Notify();

        token.HasChanged.Should().BeTrue("the token issued before Notify must fire");
        signal.GetChangeToken().HasChanged.Should().BeFalse("a fresh token is handed out after Notify");
    }

    [Fact]
    public void ChangeNotifier_IsForEntra_AndNotifyChanged_FiresSignal()
    {
        var signal = new EntraConfigChangeSignal();
        var token = signal.GetChangeToken();
        var notifier = new EntraConfigChangeNotifier(signal);

        notifier.Adapter.Should().Be(EntraAdapterConfigKeys.Adapter);
        notifier.NotifyChanged();

        token.HasChanged.Should().BeTrue();
    }

    [Fact]
    public void ChangeTokenSource_UsesDefaultName_AndSignalToken()
    {
        var signal = new EntraConfigChangeSignal();
        var source = new EntraConfigChangeTokenSource(signal);

        source.Name.Should().Be(Options.DefaultName);
        source.GetChangeToken().HasChanged.Should().BeFalse();
        signal.Notify();
        // A token captured before Notify fires; the source always reflects the signal.
        source.GetChangeToken().HasChanged.Should().BeFalse("post-Notify the source hands out a fresh token");
    }
}
