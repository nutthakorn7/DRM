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
        created.Should().NotBeNull();
        created!.TenantId.Should().Be(tenantId);
        created.WatermarkTemplateId.Should().Be(templateId);
        created.Name.Should().Be("Confidential diagonal");
        created.Pattern.Should().Be("user:{user} file:{file} time:{time}");
        created.OpacityPercent.Should().Be(33);
        created.DensityTiles.Should().Be(4);
        created.DiagonalAngleDegrees.Should().Be(-28);
        created.IncludeUserId.Should().BeTrue();
        created.IncludeTimestamp.Should().BeTrue();
        created.IncludeIpAddress.Should().BeFalse();
        created.IncludeSessionId.Should().BeFalse();
        created.RollingEnabled.Should().BeFalse();
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

    [Fact]
    public async Task Admin_can_update_anti_capture_settings_on_existing_watermark_template()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var createResponse = await CreateWatermarkTemplateAsync(
            client, tenantId, templateId, "Confidential", "user:{user}");
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var update = new
        {
            tenantId,
            name = "Confidential v2",
            pattern = "user:{user} time:{time}",
            opacityPercent = 50,
            densityTiles = 8,
            diagonalAngleDegrees = -45,
            includeUserId = true,
            includeTimestamp = true,
            includeIpAddress = true,
            includeSessionId = true,
            rollingEnabled = true
        };

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/admin/watermark-templates/{templateId}", update);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<WatermarkTemplateResponse>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Confidential v2");
        updated.OpacityPercent.Should().Be(50);
        updated.DensityTiles.Should().Be(8);
        updated.DiagonalAngleDegrees.Should().Be(-45);
        updated.IncludeIpAddress.Should().BeTrue();
        updated.IncludeSessionId.Should().BeTrue();
        updated.RollingEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_update_rejects_out_of_range_anti_capture_values()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var createResponse = await CreateWatermarkTemplateAsync(
            client, tenantId, templateId, "Confidential", "user:{user}");
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var invalid = new
        {
            tenantId,
            name = "Confidential",
            pattern = "user:{user}",
            opacityPercent = 200,
            densityTiles = 4,
            diagonalAngleDegrees = 0,
            includeUserId = true,
            includeTimestamp = true,
            includeIpAddress = false,
            includeSessionId = false,
            rollingEnabled = false
        };

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/watermark-templates/{templateId}", invalid);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse("invalid_opacity_percent"));
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
        int OpacityPercent,
        int DensityTiles,
        int DiagonalAngleDegrees,
        bool IncludeUserId,
        bool IncludeTimestamp,
        bool IncludeIpAddress,
        bool IncludeSessionId,
        bool RollingEnabled,
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
