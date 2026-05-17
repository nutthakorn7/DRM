using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminBoxIntegrationApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-box-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminBoxIntegrationApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_save_and_retrieve_box_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        var request = new
        {
            tenantId,
            clientId = "box-client",
            clientSecret = "secret",
            enterpriseId = "12345",
            webhookSecret = "whsec",
            enabled = true
        };

        using var save = await client.PutAsJsonAsync("/api/admin/box/config", request);
        save.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var loaded = await client.GetFromJsonAsync<BoxConfigResponse>(
            $"/api/admin/box/config?tenantId={tenantId}");
        loaded.Should().NotBeNull();
        loaded!.ClientId.Should().Be("box-client");
        loaded.EnterpriseId.Should().Be("12345");
        loaded.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_get_box_config_returns_404_when_unset()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/admin/box/config?tenantId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Box_test_connection_reports_missing_credentials_when_blank()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/box/config", new
        {
            tenantId,
            clientId = "",
            clientSecret = "",
            enterpriseId = "",
            webhookSecret = "",
            enabled = false
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        var result = await client.PostAsJsonAsync("/api/admin/box/test-connection", new { tenantId });
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await result.Content.ReadFromJsonAsync<BoxConnectionResponse>();
        body!.Success.Should().BeFalse();
        body.Status.Should().Be("missing_credentials");
    }

    [Fact]
    public async Task Box_webhook_rejects_request_when_tenant_header_missing()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/box/webhook", new StringContent("{}"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Box_webhook_rejects_unsigned_request()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        const string secret = "test-webhook-secret";

        using var save = await client.PutAsJsonAsync("/api/admin/box/config", new
        {
            tenantId,
            clientId = "x",
            clientSecret = "x",
            enterpriseId = "x",
            webhookSecret = secret,
            enabled = true
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        var payload = "{\"trigger\":\"FILE.UPLOADED\"}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/box/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-DRM-Tenant-Id", tenantId.ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Box_webhook_accepts_signed_request_and_stores_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        const string secret = "test-webhook-secret";

        using var save = await client.PutAsJsonAsync("/api/admin/box/config", new
        {
            tenantId,
            clientId = "x",
            clientSecret = "x",
            enterpriseId = "x",
            webhookSecret = secret,
            enabled = true
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        var payload = "{\"trigger\":\"FILE.UPLOADED\",\"source\":{\"id\":\"42\",\"name\":\"doc.pdf\"},\"created_by\":{\"login\":\"alice@example.com\"}}";
        var signature = ComputeSignature(secret, payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/box/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-DRM-Tenant-Id", tenantId.ToString());
        request.Headers.TryAddWithoutValidation("BOX-SIGNATURE-PRIMARY", signature);

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var events = await client.GetFromJsonAsync<List<BoxEventResponse>>(
            $"/api/admin/box/events?tenantId={tenantId}");
        events.Should().NotBeNull();
        events!.Should().ContainSingle();
        events[0].EventType.Should().Be("FILE.UPLOADED");
        events[0].SourceItemId.Should().Be("42");
        events[0].SourceItemName.Should().Be("doc.pdf");
        events[0].CreatedByEmail.Should().Be("alice@example.com");
    }

    private static string ComputeSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record BoxConfigResponse(
        Guid TenantId,
        string ClientId,
        string EnterpriseId,
        bool Enabled,
        string? LastConnectionStatus,
        DateTimeOffset? LastConnectionAtUtc,
        int LastWebhookEventCount,
        DateTimeOffset UpdatedAtUtc);

    private sealed record BoxConnectionResponse(bool Success, string Status, string? ErrorMessage);

    private sealed record BoxEventResponse(
        long Id,
        string EventType,
        string SourceItemId,
        string SourceItemName,
        string? CreatedByEmail,
        DateTimeOffset ReceivedAtUtc);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Upsert_box_config_with_mismatched_header_returns_400()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/box/config")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                clientId = "id",
                clientSecret = "secret",
                enterpriseId = "ent",
                webhookSecret = "hook",
                enabled = true,
            })
        };
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record ErrorBody(string ReasonCode);
}
