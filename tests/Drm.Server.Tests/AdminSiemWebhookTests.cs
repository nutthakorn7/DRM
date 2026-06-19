using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Drm.Server;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Drm.Server.Tests;

public sealed class AdminSiemWebhookTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-siem-webhooks-{Guid.NewGuid():N}.db");
    private readonly RecordingSiemEventSink siemSink = new();
    private readonly WebApplicationFactory<Program> factory;

    public AdminSiemWebhookTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISiemEventSink>();
                    services.AddSingleton<ISiemEventSink>(siemSink);
                });
            });
    }

    [Fact]
    public async Task Enabled_webhook_receives_file_registered_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var createWebhook = await CreateWebhookAsync(client, tenantId, webhookId, "https://1.1.1.1/events", enabled: true);
        createWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId);

        register.StatusCode.Should().Be(HttpStatusCode.Created);
        siemSink.Deliveries.Should().ContainSingle();
        siemSink.Deliveries[0].Webhook.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            WebhookId = webhookId,
            Url = "https://1.1.1.1/events",
            Enabled = true
        });
        siemSink.Deliveries[0].AuditEvent.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            FileId = (Guid?)fileId,
            UserId = (Guid?)ownerUserId,
            EventType = "file_registered",
            ReasonCode = "registered"
        });
    }

    [Fact]
    public async Task Enabled_webhook_receives_file_revoked_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var createWebhook = await CreateWebhookAsync(client, tenantId, Guid.NewGuid(), "https://1.1.1.1/events", enabled: true);
        createWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId);
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        siemSink.Clear();

        using var revoke = await client.PostAsync($"/api/files/{fileId}/revoke?tenantId={tenantId}", content: null);

        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        siemSink.Deliveries.Should().ContainSingle(delivery =>
            delivery.AuditEvent.EventType == "file_revoked" &&
            delivery.AuditEvent.ReasonCode == "revoked" &&
            delivery.AuditEvent.TenantId == tenantId &&
            delivery.AuditEvent.FileId == fileId &&
            delivery.AuditEvent.UserId == ownerUserId);
    }

    [Fact]
    public async Task Disabled_webhook_does_not_receive_file_events()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var createWebhook = await CreateWebhookAsync(client, tenantId, Guid.NewGuid(), "https://1.1.1.1/events", enabled: false);
        createWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await RegisterFileAsync(client, tenantId, Guid.NewGuid(), Guid.NewGuid());

        register.StatusCode.Should().Be(HttpStatusCode.Created);
        siemSink.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task Other_tenant_webhook_does_not_receive_file_events()
    {
        using var client = factory.CreateClient();
        var fileTenantId = Guid.NewGuid();
        var webhookTenantId = Guid.NewGuid();

        using var createWebhook = await CreateWebhookAsync(client, webhookTenantId, Guid.NewGuid(), "https://1.1.1.1/events", enabled: true);
        createWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await RegisterFileAsync(client, fileTenantId, Guid.NewGuid(), Guid.NewGuid());

        register.StatusCode.Should().Be(HttpStatusCode.Created);
        siemSink.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task Failing_webhook_does_not_fail_file_registration_or_block_later_webhooks()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var failingWebhookId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var succeedingWebhookId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        using var failingWebhook = await CreateWebhookAsync(client, tenantId, failingWebhookId, "https://8.8.8.8/events", enabled: true);
        using var succeedingWebhook = await CreateWebhookAsync(client, tenantId, succeedingWebhookId, "https://1.1.1.1/events", enabled: true);
        failingWebhook.StatusCode.Should().Be(HttpStatusCode.Created);
        succeedingWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        siemSink.FailingUrls.Add("https://8.8.8.8/events");

        using var register = await RegisterFileAsync(client, tenantId, Guid.NewGuid(), Guid.NewGuid());

        register.StatusCode.Should().Be(HttpStatusCode.Created);
        siemSink.Deliveries.Should().ContainSingle(delivery => delivery.Webhook.Url == "https://1.1.1.1/events");
    }

    [Fact]
    public async Task Duplicate_file_registration_does_not_dispatch_second_siem_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var createWebhook = await CreateWebhookAsync(client, tenantId, Guid.NewGuid(), "https://1.1.1.1/events", enabled: true);
        createWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var firstRegister = await RegisterFileAsync(client, tenantId, fileId, ownerUserId);
        firstRegister.StatusCode.Should().Be(HttpStatusCode.Created);
        siemSink.Clear();

        using var duplicateRegister = await RegisterFileAsync(client, tenantId, fileId, ownerUserId);

        duplicateRegister.StatusCode.Should().Be(HttpStatusCode.Conflict);
        siemSink.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_file_registration_does_not_dispatch_siem_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var createWebhook = await CreateWebhookAsync(client, tenantId, Guid.NewGuid(), "https://1.1.1.1/events", enabled: true);
        createWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId = Guid.NewGuid(),
            ownerUserId = Guid.NewGuid(),
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "Fly",
            watermarkTemplate = "user:{userId}"
        });

        register.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        siemSink.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_can_create_and_list_siem_webhooks_for_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var sharedWebhookId = Guid.NewGuid();
        var secondWebhookId = Guid.NewGuid();

        using var firstCreate = await CreateWebhookAsync(client, tenantId, sharedWebhookId, "https://8.8.8.8/events", enabled: true);
        using var secondCreate = await CreateWebhookAsync(client, tenantId, secondWebhookId, "https://1.1.1.1/events", enabled: false);
        using var otherTenantCreate = await CreateWebhookAsync(client, otherTenantId, sharedWebhookId, "https://9.9.9.9/events", enabled: true);

        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        secondCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        otherTenantCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await firstCreate.Content.ReadFromJsonAsync<SiemWebhookResponse>();
        created.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            WebhookId = sharedWebhookId,
            Url = "https://8.8.8.8/events",
            Enabled = true
        });
        created!.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var webhooks = await client.GetFromJsonAsync<List<SiemWebhookResponse>>($"/api/admin/siem-webhooks?tenantId={tenantId}");

        webhooks.Should().NotBeNull();
        webhooks!.Select(webhook => webhook.Url).Should().Equal("https://1.1.1.1/events", "https://8.8.8.8/events");
        webhooks.Select(webhook => webhook.WebhookId).Should().Equal(secondWebhookId, sharedWebhookId);
        webhooks.Should().OnlyContain(webhook => webhook.TenantId == tenantId);
    }

    [Fact]
    public async Task Admin_can_delete_a_siem_webhook_and_delete_is_tenant_scoped()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();

        using (var create = await CreateWebhookAsync(client, tenantId, webhookId, "https://1.1.1.1/events", enabled: true))
            create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Scoped on {tenantId, webhookId}: a different tenant can't delete it.
        using (var wrongTenant = await client.DeleteAsync($"/api/admin/siem-webhooks/{webhookId}?tenantId={otherTenantId}"))
            wrongTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var delete = await client.DeleteAsync($"/api/admin/siem-webhooks/{webhookId}?tenantId={tenantId}"))
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remaining = await client.GetFromJsonAsync<List<SiemWebhookResponse>>(
            $"/api/admin/siem-webhooks?tenantId={tenantId}");
        remaining.Should().BeEmpty();

        // Idempotency: deleting an already-gone webhook is a clean 404.
        using var deleteAgain = await client.DeleteAsync($"/api/admin/siem-webhooks/{webhookId}?tenantId={tenantId}");
        deleteAgain.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_create_siem_webhook_returns_conflict_for_duplicate_webhook_id_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();

        using var firstCreate = await CreateWebhookAsync(client, tenantId, webhookId, "https://1.1.1.1/events", enabled: true);
        using var duplicateCreate = await CreateWebhookAsync(client, tenantId, webhookId, "https://9.9.9.9/events", enabled: true);

        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_create_siem_webhook_rejects_invalid_admin_state()
    {
        using var client = factory.CreateClient();

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.Empty,
            webhookId = Guid.NewGuid(),
            url = "https://1.1.1.1/events",
            enabled = true
        }, "invalid_tenant_id");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.Empty,
            url = "https://1.1.1.1/events",
            enabled = true
        }, "invalid_webhook_id");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = (string?)null,
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://example.com/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "http://siem.example.test/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://localhost./events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://127.0.0.1/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://10.0.0.5/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://0.0.0.0/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://100.64.0.1/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "https://169.254.169.254/latest/meta-data",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = "ftp://siem.example.test/events",
            enabled = true
        }, "invalid_url");

        await AssertBadRequestAsync(client, new
        {
            tenantId = Guid.NewGuid(),
            webhookId = Guid.NewGuid(),
            url = new string('u', 2049),
            enabled = true
        }, "invalid_url");
    }

    [Fact]
    public async Task Admin_list_siem_webhooks_rejects_empty_tenant_id()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/admin/siem-webhooks?tenantId={Guid.Empty}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse("invalid_tenant_id"));
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private static Task<HttpResponseMessage> CreateWebhookAsync(
        HttpClient client,
        Guid tenantId,
        Guid webhookId,
        string url,
        bool enabled)
    {
        return client.PostAsJsonAsync("/api/admin/siem-webhooks", new
        {
            tenantId,
            webhookId,
            url,
            enabled
        });
    }

    private static Task<HttpResponseMessage> RegisterFileAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId)
    {
        return client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "user:{userId}"
        });
    }

    private static async Task AssertBadRequestAsync(
        HttpClient client,
        object request,
        string reasonCode)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/siem-webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse(reasonCode));
    }

    private sealed record SiemWebhookResponse(
        Guid TenantId,
        Guid WebhookId,
        string Url,
        bool Enabled,
        DateTimeOffset CreatedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);

    private sealed class RecordingSiemEventSink : ISiemEventSink
    {
        private readonly object sync = new();
        private readonly List<SiemDelivery> deliveries = [];

        public HashSet<string> FailingUrls { get; } = [];

        public IReadOnlyList<SiemDelivery> Deliveries
        {
            get
            {
                lock (sync)
                {
                    return deliveries.ToArray();
                }
            }
        }

        public Task SendAsync(SiemWebhookEntity webhook, AuditEventEntity auditEvent, CancellationToken cancellationToken)
        {
            if (FailingUrls.Contains(webhook.Url))
            {
                throw new HttpRequestException("simulated SIEM failure");
            }

            lock (sync)
            {
                deliveries.Add(new SiemDelivery(webhook, auditEvent));
            }

            return Task.CompletedTask;
        }

        public void Clear()
        {
            lock (sync)
            {
                deliveries.Clear();
                FailingUrls.Clear();
            }
        }
    }

    private sealed record SiemDelivery(SiemWebhookEntity Webhook, AuditEventEntity AuditEvent);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Create_siem_webhook_with_mismatched_header_returns_400()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/siem-webhooks")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                webhookId = Guid.NewGuid(),
                url = "https://siem.example.com/ingest",
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
