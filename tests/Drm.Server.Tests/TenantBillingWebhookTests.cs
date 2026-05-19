using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class TenantBillingWebhookTests : IDisposable
{
    private const string AdminApiKey = "billing-webhook-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-billing-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public TenantBillingWebhookTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
            });
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_webhook_returns_created_with_secret()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/billing-webhooks",
            new { url = "https://hooks.example.com/drm", events = "*" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<WebhookCreateBody>();
        body!.TenantId.Should().Be(tenantId);
        body.Url.Should().Be("https://hooks.example.com/drm");
        body.Events.Should().Be("*");
        body.Secret.Should().NotBeNullOrWhiteSpace();
        body.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Create_webhook_with_specific_events_stores_them()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/billing-webhooks",
            new { url = "https://hooks.example.com/drm", events = "seat_limit_approach" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<WebhookCreateBody>();
        body!.Events.Should().Be("seat_limit_approach");
    }

    [Fact]
    public async Task Create_webhook_with_invalid_url_returns_bad_request()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/billing-webhooks",
            new { url = "not-a-url" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_webhook_for_unknown_tenant_returns_not_found()
    {
        using var client = MakeAdminClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{Guid.NewGuid()}/billing-webhooks",
            new { url = "https://hooks.example.com/drm" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_webhooks_returns_registered_webhooks()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);

        await CreateWebhook(client, tenantId, "https://hooks.example.com/a");
        await CreateWebhook(client, tenantId, "https://hooks.example.com/b");

        using var response = await client.GetAsync($"/api/admin/tenants/{tenantId}/billing-webhooks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var hooks = await response.Content.ReadFromJsonAsync<List<WebhookBody>>();
        hooks.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_webhooks_does_not_include_secret()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);
        await CreateWebhook(client, tenantId, "https://hooks.example.com/drm");

        using var response = await client.GetAsync($"/api/admin/tenants/{tenantId}/billing-webhooks");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("\"secret\"");
    }

    [Fact]
    public async Task Delete_webhook_returns_no_content()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);
        var created = await CreateWebhook(client, tenantId, "https://hooks.example.com/drm");

        using var response = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/billing-webhooks/{created.WebhookId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_webhook_removes_it_from_list()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);
        var created = await CreateWebhook(client, tenantId, "https://hooks.example.com/drm");

        await client.DeleteAsync($"/api/admin/tenants/{tenantId}/billing-webhooks/{created.WebhookId}");

        using var response = await client.GetAsync($"/api/admin/tenants/{tenantId}/billing-webhooks");
        var hooks = await response.Content.ReadFromJsonAsync<List<WebhookBody>>();
        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_unknown_webhook_returns_not_found()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);
        using var response = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/billing-webhooks/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    private async Task<Guid> CreateTenant(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        var r = await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"t-{tenantId:N}", tenantId });
        r.EnsureSuccessStatusCode();
        return tenantId;
    }

    private async Task<WebhookCreateBody> CreateWebhook(HttpClient client, Guid tenantId, string url, string events = "*")
    {
        var r = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/billing-webhooks",
            new { url, events });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<WebhookCreateBody>())!;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record WebhookBody(
        Guid WebhookId, Guid TenantId, string Url, string Events, bool Enabled, DateTimeOffset CreatedAtUtc);

    private sealed record WebhookCreateBody(
        Guid WebhookId, Guid TenantId, string Url, string Events, bool Enabled, DateTimeOffset CreatedAtUtc, string Secret);
}
