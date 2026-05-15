using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminWatermarkTemplatesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-watermark-templates-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminWatermarkTemplatesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_create_get_and_list_watermark_templates_for_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherTenantTemplateId = Guid.NewGuid();

        using var otherTenantCreate = await CreateWatermarkTemplateAsync(
            client,
            otherTenantId,
            otherTenantTemplateId,
            "Alpha",
            "other:{user}");
        otherTenantCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var create = await CreateWatermarkTemplateAsync(
            client,
            tenantId,
            templateId,
            "Confidential diagonal",
            "user:{user} file:{file} time:{time}");

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.Location.Should().NotBeNull();

        var created = await create.Content.ReadFromJsonAsync<WatermarkTemplateResponse>();
        created.Should().BeEquivalentTo(new WatermarkTemplateResponse(
            tenantId,
            templateId,
            "Confidential diagonal",
            "user:{user} file:{file} time:{time}",
            created!.CreatedAtUtc));
        created.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        using var getCreated = await client.GetAsync(create.Headers.Location);
        getCreated.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getCreated.Content.ReadFromJsonAsync<WatermarkTemplateResponse>();
        fetched.Should().BeEquivalentTo(created);

        using var secondCreate = await CreateWatermarkTemplateAsync(
            client,
            tenantId,
            Guid.NewGuid(),
            "Alpha",
            "alpha:{user}");
        secondCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        var templates = await client.GetFromJsonAsync<List<WatermarkTemplateResponse>>(
            $"/api/admin/watermark-templates?tenantId={tenantId}");

        templates.Should().NotBeNull();
        templates!.Select(template => template.Name).Should().Equal("Alpha", "Confidential diagonal");
        templates.Should().OnlyContain(template => template.TenantId == tenantId);

        using var auditResponse = await client.GetAsync($"/api/audit?tenantId={tenantId}");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<AuditEventResponse>>();
        auditEvents.Should().NotBeNull();
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "system_changed" &&
            auditEvent.ReasonCode == "watermark_template_created");
    }

    [Fact]
    public async Task Admin_create_watermark_template_returns_conflict_for_duplicate_id_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var firstCreate = await CreateWatermarkTemplateAsync(client, tenantId, templateId, "Confidential", "user:{user}");
        using var duplicateCreate = await CreateWatermarkTemplateAsync(client, tenantId, templateId, "Restricted", "file:{file}");

        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_create_watermark_template_rejects_invalid_admin_state()
    {
        using var client = factory.CreateClient();

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.Empty,
                watermarkTemplateId = Guid.NewGuid(),
                name = "Confidential",
                pattern = "user:{user}"
            },
            "invalid_tenant_id");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                watermarkTemplateId = Guid.Empty,
                name = "Confidential",
                pattern = "user:{user}"
            },
            "invalid_watermark_template_id");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                watermarkTemplateId = Guid.NewGuid(),
                name = " ",
                pattern = "user:{user}"
            },
            "invalid_name");

        await AssertBadRequestAsync(
            client,
            new
            {
                tenantId = Guid.NewGuid(),
                watermarkTemplateId = Guid.NewGuid(),
                name = "Confidential",
                pattern = " "
            },
            "invalid_pattern");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static Task<HttpResponseMessage> CreateWatermarkTemplateAsync(
        HttpClient client,
        Guid tenantId,
        Guid templateId,
        string name,
        string pattern)
    {
        return client.PostAsJsonAsync("/api/admin/watermark-templates", new
        {
            tenantId,
            watermarkTemplateId = templateId,
            name,
            pattern
        });
    }

    private static async Task AssertBadRequestAsync(HttpClient client, object request, string reasonCode)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/watermark-templates", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse(reasonCode));
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

    private sealed record WatermarkTemplateResponse(
        Guid TenantId,
        Guid WatermarkTemplateId,
        string Name,
        string Pattern,
        DateTimeOffset CreatedAtUtc);

    private sealed record AuditEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
