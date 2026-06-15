using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminPolicyTemplatesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-policy-templates-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminPolicyTemplatesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_create_and_list_policy_templates_for_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var create = await CreatePolicyTemplateAsync(
            client,
            tenantId,
            templateId,
            "Confidential",
            "view, print",
            "user:{userId}",
            120,
            true);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.Location.Should().NotBeNull();

        var created = await create.Content.ReadFromJsonAsync<PolicyTemplateResponse>();
        created.Should().NotBeNull();
        created.Should().BeEquivalentTo(new PolicyTemplateResponse(
            tenantId,
            templateId,
            "Confidential",
            "View, Print",
            "user:{userId}",
            120,
            true));

        using var getCreated = await client.GetAsync(create.Headers.Location);
        getCreated.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getCreated.Content.ReadFromJsonAsync<PolicyTemplateResponse>();
        fetched.Should().BeEquivalentTo(created);

        var templates = await client.GetFromJsonAsync<List<PolicyTemplateResponse>>(
            $"/api/admin/policy-templates?tenantId={tenantId}");

        templates.Should().NotBeNull();
        templates.Should().ContainSingle(template => template.TemplateId == templateId && template.Permissions == "View, Print");

        using var auditResponse = await client.GetAsync($"/api/audit?tenantId={tenantId}");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<AuditEventResponse>>();

        auditEvents.Should().NotBeNull();
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "system_changed" &&
            auditEvent.ReasonCode == "policy_template_created" &&
            auditEvent.UserId == null);
    }

    [Fact]
    public async Task Admin_create_policy_template_returns_conflict_for_duplicate_template_id_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var firstCreate = await CreatePolicyTemplateAsync(client, tenantId, templateId, "Confidential");
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicateCreate = await CreatePolicyTemplateAsync(client, tenantId, templateId, "Restricted");

        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_can_create_same_template_id_in_different_tenants()
    {
        using var client = factory.CreateClient();
        var templateId = Guid.NewGuid();

        using var firstCreate = await CreatePolicyTemplateAsync(client, Guid.NewGuid(), templateId, "Confidential");
        using var secondCreate = await CreatePolicyTemplateAsync(client, Guid.NewGuid(), templateId, "Restricted");

        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        secondCreate.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Admin_create_policy_template_with_invalid_permissions_returns_bad_request()
    {
        using var client = factory.CreateClient();

        using var response = await CreatePolicyTemplateAsync(
            client,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Confidential",
            "View, Fly");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse("invalid_permissions"));
    }

    [Fact]
    public async Task Admin_create_policy_template_rejects_invalid_admin_state()
    {
        using var client = factory.CreateClient();

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.Empty,
                templateId = Guid.NewGuid(),
                name = "Confidential",
                permissions = "View",
                watermarkTemplate = "user:{userId}",
                offlineLeaseMinutes = 60,
                allowPrint = false
            },
            "invalid_tenant_id");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                templateId = Guid.Empty,
                name = "Confidential",
                permissions = "View",
                watermarkTemplate = "user:{userId}",
                offlineLeaseMinutes = 60,
                allowPrint = false
            },
            "invalid_template_id");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                templateId = Guid.NewGuid(),
                name = " ",
                permissions = "View",
                watermarkTemplate = "user:{userId}",
                offlineLeaseMinutes = 60,
                allowPrint = false
            },
            "invalid_name");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                templateId = Guid.NewGuid(),
                name = new string('n', 257),
                permissions = "View",
                watermarkTemplate = "user:{userId}",
                offlineLeaseMinutes = 60,
                allowPrint = false
            },
            "invalid_name");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                templateId = Guid.NewGuid(),
                name = "Confidential",
                permissions = "View",
                watermarkTemplate = new string('w', 1025),
                offlineLeaseMinutes = 60,
                allowPrint = false
            },
            "invalid_watermark_template");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                templateId = Guid.NewGuid(),
                name = "Confidential",
                permissions = "View",
                watermarkTemplate = "user:{userId}",
                offlineLeaseMinutes = -1,
                allowPrint = false
            },
            "invalid_offline_lease_minutes");
    }

    [Fact]
    public async Task Admin_list_policy_templates_is_scoped_to_tenant_and_ordered_by_name()
    {
        using var client = factory.CreateClient();
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var firstTenantLowNameTemplateId = Guid.NewGuid();
        var firstTenantHighNameTemplateId = Guid.NewGuid();
        var secondTenantTemplateId = Guid.NewGuid();

        using var firstCreate = await CreatePolicyTemplateAsync(
            client,
            firstTenantId,
            firstTenantHighNameTemplateId,
            "Zulu");
        using var secondFirstTenantCreate = await CreatePolicyTemplateAsync(
            client,
            firstTenantId,
            firstTenantLowNameTemplateId,
            "Alpha");
        using var secondCreate = await CreatePolicyTemplateAsync(
            client,
            secondTenantId,
            secondTenantTemplateId,
            "Bravo");

        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        secondFirstTenantCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        secondCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        var templates = await client.GetFromJsonAsync<List<PolicyTemplateResponse>>(
            $"/api/admin/policy-templates?tenantId={firstTenantId}");

        templates.Should().NotBeNull();
        templates!.Select(template => template.Name).Should().Equal("Alpha", "Zulu");
        templates.Select(template => template.TemplateId).Should().Equal(firstTenantLowNameTemplateId, firstTenantHighNameTemplateId);
        templates.Should().NotContain(template => template.TemplateId == secondTenantTemplateId);
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

    [Fact]
    public async Task Admin_can_update_a_policy_template()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using (var create = await CreatePolicyTemplateAsync(client, tenantId, templateId, "Original",
            permissions: "View", offlineLeaseMinutes: 60, allowPrint: false))
            create.StatusCode.Should().Be(HttpStatusCode.Created);

        using var update = await client.PutAsJsonAsync($"/api/admin/policy-templates/{templateId}", new
        {
            tenantId,
            name = "Renamed",
            permissions = "View, Print",
            watermarkTemplate = "tenant:{tenantId}",
            offlineLeaseMinutes = 120,
            allowPrint = true,
            maxOpens = 5,
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await update.Content.ReadFromJsonAsync<PolicyTemplateResponse>();
        updated.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            TemplateId = templateId,
            Name = "Renamed",
            Permissions = "View, Print",
            WatermarkTemplate = "tenant:{tenantId}",
            OfflineLeaseMinutes = 120,
            AllowPrint = true,
        });

        // Persisted (read back via list).
        var templates = await client.GetFromJsonAsync<List<PolicyTemplateResponse>>(
            $"/api/admin/policy-templates?tenantId={tenantId}");
        templates!.Single().Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Admin_update_missing_policy_template_returns_not_found()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var update = await client.PutAsJsonAsync($"/api/admin/policy-templates/{Guid.NewGuid()}", new
        {
            tenantId,
            name = "Nope",
            permissions = "View",
            watermarkTemplate = "user:{userId}",
            offlineLeaseMinutes = 30,
            allowPrint = false,
        });
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> CreatePolicyTemplateAsync(
        HttpClient client,
        Guid tenantId,
        Guid templateId,
        string name,
        string permissions = "View",
        string watermarkTemplate = "user:{userId}",
        int offlineLeaseMinutes = 60,
        bool allowPrint = false)
    {
        return client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name,
            permissions,
            watermarkTemplate,
            offlineLeaseMinutes,
            allowPrint
        });
    }

    private static async Task AssertBadRequestAsync(
        HttpClient client,
        object request,
        string reasonCode)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/policy-templates", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse(reasonCode));
    }

    private sealed record PolicyTemplateResponse(
        Guid TenantId,
        Guid TemplateId,
        string Name,
        string Permissions,
        string WatermarkTemplate,
        int OfflineLeaseMinutes,
        bool AllowPrint);

    private sealed record ErrorResponse(string ReasonCode);

    private sealed record AuditEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Create_policy_template_with_mismatched_header_returns_400()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/policy-templates")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                templateId = Guid.NewGuid(),
                name = "Confidential",
                permissions = "View",
                watermarkTemplate = "",
                offlineLeaseMinutes = 60,
                allowPrint = false,
            })
        };
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }
}
