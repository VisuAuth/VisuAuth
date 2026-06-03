using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Configuration;
using Xunit;

namespace VisuAuth.IntegrationTests.Admin;

/// <summary>
/// Integration tests for <c>/visuauth/admin/entra-config</c>. The default
/// (Identity) sample wires no adapter-config store, so the page renders the
/// "not enabled" explainer; a second server injects a schema + store to render
/// the full editor and exercise the save round-trip through the real pipeline.
/// </summary>
public sealed partial class EntraConfigPageTests(VisuAuthTestFactory factory) : IClassFixture<VisuAuthTestFactory>
{
    private const string Url = "/visuauth/admin/entra-config";

    private readonly VisuAuthTestFactory _factory = factory;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex TokenRegexImpl();

    [Fact]
    public async Task GetEntraConfig_WithoutStore_RendersNotEnabledExplainer_AndHidesSidebarLink()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await client.GetAsync(new Uri(Url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("isn't enabled", "the Identity sample wires no adapter-config store");
        body.Should().NotContain($"href=\"{Url}\"", "the sidebar link is hidden when no schema/store is wired");
    }

    [Fact]
    public async Task GetEntraConfig_WithSchemaAndStore_RendersEditor_WithBadgesAndSecretField()
    {
        using var client = CreateConfiguredClient();

        var response = await client.GetAsync(new Uri(Url, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("Tenant ID", "the schema field label renders");
        body.Should().Contain("From DB", "a stored value shows the DB source badge");
        body.Should().Contain("type=\"password\"", "the secret field is write-only");
        body.Should().Contain($"href=\"{Url}\"", "the sidebar link shows once a schema + store are wired");
    }

    [Fact]
    public async Task PostSave_PersistsThroughStore_AndRendersSuccess()
    {
        using var client = CreateConfiguredClient();

        var getBody = await (await client.GetAsync(new Uri(Url, UriKind.Relative))).Content.ReadAsStringAsync();
        var token = TokenRegexImpl().Match(getBody).Groups[1].Value;
        token.Should().NotBeNullOrEmpty();

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("adapter", "Entra"),
            new KeyValuePair<string, string>("FieldValues[TenantId]", "new-tenant"),
            new KeyValuePair<string, string>("FieldValues[ClientSecret]", string.Empty),
        });

        var response = await client.PostAsync(new Uri($"{Url}?handler=Save", UriKind.Relative), form);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("saved", "the success banner renders after a save");
        RecordingStore.LastCommand.Should().NotBeNull();
        RecordingStore.LastCommand!.Adapter.Should().Be("Entra");
        RecordingStore.LastCommand.Values.Single(v => v.Key == "TenantId").Value.Should().Be("new-tenant");
    }

    private HttpClient CreateConfiguredClient()
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAdapterConfigSchema, FakeSchema>();
                services.AddSingleton<IAdapterConfigStore, RecordingStore>();
            }))
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private sealed class FakeSchema : IAdapterConfigSchema
    {
        public string Adapter => "Entra";
        public string DisplayName => "Microsoft Entra ID";
        public IReadOnlyList<AdapterConfigField> Fields { get; } =
        [
            new() { Key = "TenantId", Label = "Tenant ID", IsRequired = true },
            new() { Key = "ClientSecret", Label = "Client secret", IsSecret = true, IsRequired = true },
        ];
        public bool HasCodeValue(string key) => false;
        public string? GetCodeValue(string key) => null;
    }

    // Singleton across the configured server so the GET (render) and POST
    // (save) in PostSave share the same recorded state.
    private sealed class RecordingStore : IAdapterConfigStore
    {
        public static SaveAdapterConfigCommand? LastCommand { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> GetResolvedAsync(string adapter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<IReadOnlyList<AdapterConfigEntryView>> ListAsync(string adapter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AdapterConfigEntryView>>(
            [
                new() { Key = "TenantId", IsSecret = false, HasValue = true, Value = "stored-tenant" },
                new() { Key = "ClientSecret", IsSecret = true, HasValue = true, Value = null },
            ]);

        public Task<StoreResult> SaveAsync(SaveAdapterConfigCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(StoreResult.Success());
        }
    }
}
