using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AgentDiscoverApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-agent-discover-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AgentDiscoverApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:KeyWrapping:MasterKeyBase64",
                    Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()));
            });
    }

    [Fact]
    public async Task Discover_returns_identity_for_registered_user_email()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await CreateUserAsync(client, tenantId, userId, "alice@example.test", "Alice Tester");

        using var response = await client.GetAsync(
            $"/api/agent/discover?email=alice@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
        body!.TenantId.Should().Be(tenantId);
        body.UserId.Should().Be(userId);
        body.DisplayName.Should().Be("Alice Tester");
        body.Email.Should().Be("alice@example.test");
        body.DefaultPolicyTemplateId.Should().BeNull(
            "no templates exist in this fresh tenant — server falls back to null");
        body.DefaultExpiryDays.Should().Be(7);
    }

    [Fact]
    public async Task Discover_is_case_insensitive_on_the_email_input()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // User stored with lowercase email
        await CreateUserAsync(client, tenantId, userId, "bob@example.test", "Bob");

        // Look up with MIXED CASE — must match
        using var response = await client.GetAsync(
            "/api/agent/discover?email=Bob@Example.TEST");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
        body!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Discover_returns_default_template_id_when_tenant_has_templates()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var oldTemplateId = Guid.NewGuid();
        var newTemplateId = Guid.NewGuid();

        await CreateUserAsync(client, tenantId, userId, "carol@example.test", "Carol");

        // First template — should NOT win because there's a newer one
        await CreateTemplateAsync(client, tenantId, oldTemplateId, "Old template");
        // Tiny delay so CreatedAtUtc is strictly greater on the new one
        await Task.Delay(20);
        await CreateTemplateAsync(client, tenantId, newTemplateId, "New template");

        using var response = await client.GetAsync(
            "/api/agent/discover?email=carol@example.test");

        var body = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
        body!.DefaultPolicyTemplateId.Should().Be(newTemplateId,
            "most recently created template wins as the default");
    }

    [Fact]
    public async Task Discover_returns_404_for_unknown_email()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/agent/discover?email=nobody@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@")]
    [InlineData("ab")]
    public async Task Discover_returns_400_for_invalid_email(string badEmail)
    {
        using var client = factory.CreateClient();
        // Use raw URL so we can pass empty/whitespace through query
        var encoded = Uri.EscapeDataString(badEmail);
        using var response = await client.GetAsync(
            $"/api/agent/discover?email={encoded}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Discover_ignores_inactive_users()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await CreateUserAsync(client, tenantId, userId, "deleted@example.test", "Deleted");

        // Flip Active=false directly via the service-scope DB context. There
        // is no public /api/admin/users/{id}/disable endpoint yet — when one
        // lands, swap this for the HTTP call.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = db.TenantUsers.Single(x => x.TenantId == tenantId && x.UserId == userId);
            u.Active = false;
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync(
            "/api/agent/discover?email=deleted@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "inactive users should not be discoverable so an offboarded employee's machine cannot keep using DRM");
    }

    [Fact]
    public async Task Discover_does_not_leak_other_tenant_data()
    {
        using var client = factory.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userIdA = Guid.NewGuid();

        // User exists in tenantA only — same email registered nowhere else
        await CreateUserAsync(client, tenantA, userIdA, "alice@acme.test", "Alice");

        // Tenant B has its own templates but no Alice
        var templateInB = Guid.NewGuid();
        await CreateTemplateAsync(client, tenantB, templateInB, "B's template");

        using var response = await client.GetAsync(
            "/api/agent/discover?email=alice@acme.test");

        var body = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
        body!.TenantId.Should().Be(tenantA);
        body.DefaultPolicyTemplateId.Should().BeNull(
            "tenant A has no templates; B's templates must not leak across tenants");
    }

    private static async Task CreateUserAsync(
        HttpClient client, Guid tenantId, Guid userId, string email, string displayName)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/users", new
        {
            tenantId,
            userId,
            email,
            displayName
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private static async Task CreateTemplateAsync(
        HttpClient client, Guid tenantId, Guid templateId, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name,
            permissions = "View",
            watermarkTemplate = "user:{userId}",
            offlineLeaseMinutes = 15,
            allowPrint = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record DiscoveryResponse(
        Guid TenantId,
        Guid UserId,
        string DisplayName,
        string Email,
        Guid? DefaultPolicyTemplateId,
        int DefaultExpiryDays);
}
