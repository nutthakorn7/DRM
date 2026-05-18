using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class TenantSuspensionTests : IDisposable
{
    private const string AdminApiKey = "suspension-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-suspension-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public TenantSuspensionTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
            });
    }

    // ── Suspension enforcement ────────────────────────────────────────────────

    [Fact]
    public async Task Active_tenant_allows_api_requests()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateActiveTenant(client);

        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        using var response = await client.GetAsync($"/api/admin/users?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspended_tenant_gets_403_on_api_requests()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateActiveTenant(client);
        await SuspendTenant(client, tenantId);

        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        using var response = await client.GetAsync($"/api/admin/users?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_suspended");
    }

    [Fact]
    public async Task Reactivated_tenant_allows_api_requests_again()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateActiveTenant(client);
        await SuspendTenant(client, tenantId);
        await ActivateTenant(client, tenantId);

        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        using var response = await client.GetAsync($"/api/admin/users?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Requests_without_tenant_header_are_not_blocked_by_suspension()
    {
        // Healthz and other endpoints without the header should always work
        using var client = MakeAdminClient();
        using var response = await client.GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Seat quota ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_user_when_seats_full_returns_conflict_seat_limit_exceeded()
    {
        using var client = MakeAdminClient();
        var tenantId = Guid.NewGuid();
        // Create tenant with MaxEncrypters = 1
        await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"seats-{tenantId:N}", tenantId, maxEncrypters = 1 });

        // First user succeeds
        var user1 = new
        {
            tenantId,
            userId = Guid.NewGuid(),
            email = $"u1-{Guid.NewGuid():N}@example.com",
            displayName = "User 1"
        };
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        var first = await client.PostAsJsonAsync("/api/admin/users", user1);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second user is blocked
        var user2 = new
        {
            tenantId,
            userId = Guid.NewGuid(),
            email = $"u2-{Guid.NewGuid():N}@example.com",
            displayName = "User 2"
        };
        using var second = await client.PostAsJsonAsync("/api/admin/users", user2);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("seat_limit_exceeded");
    }

    [Fact]
    public async Task Creating_user_when_no_seat_limit_always_succeeds()
    {
        using var client = MakeAdminClient();
        var tenantId = Guid.NewGuid();
        // Create tenant with no MaxEncrypters
        await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"unlimited-{tenantId:N}", tenantId });

        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostAsJsonAsync("/api/admin/users", new
            {
                tenantId,
                userId = Guid.NewGuid(),
                email = $"u{i}-{Guid.NewGuid():N}@example.com",
                displayName = $"User {i}"
            });
            r.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    private async Task<Guid> CreateActiveTenant(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        var r = await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"t-{tenantId:N}", tenantId });
        r.EnsureSuccessStatusCode();
        return tenantId;
    }

    private async Task SuspendTenant(HttpClient client, Guid tenantId)
    {
        var r = await client.PatchAsJsonAsync($"/api/admin/tenants/{tenantId}", new { status = 1 });
        r.EnsureSuccessStatusCode();
    }

    private async Task ActivateTenant(HttpClient client, Guid tenantId)
    {
        var r = await client.PatchAsJsonAsync($"/api/admin/tenants/{tenantId}", new { status = 0 });
        r.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record ErrorBody(string ReasonCode);
}
