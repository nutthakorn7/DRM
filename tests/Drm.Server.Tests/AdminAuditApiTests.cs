using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AdminAuditApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-audit-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminAuditApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_audit_json_filters_by_tenant_and_event_type()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var permissionEvent = NewAuditEvent(tenantId, "permission_changed", "file_grant_upserted");
        var systemEvent = NewAuditEvent(tenantId, "system_changed", "group_created");
        var otherTenantEvent = NewAuditEvent(otherTenantId, "permission_changed", "other_tenant");
        await SeedAuditEventsAsync(permissionEvent, systemEvent, otherTenantEvent);

        var response = await client.GetFromJsonAsync<List<AuditEventResponse>>(
            $"/api/admin/audit?tenantId={tenantId}&eventType=permission_changed");

        response.Should().NotBeNull();
        response.Should().ContainSingle();
        response![0].TenantId.Should().Be(tenantId);
        response[0].EventType.Should().Be("permission_changed");
        response[0].ReasonCode.Should().Be("file_grant_upserted");
    }

    [Fact]
    public async Task Admin_audit_json_blank_event_type_does_not_filter_events()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedAuditEventsAsync(
            NewAuditEvent(tenantId, "permission_changed", "file_grant_upserted"),
            NewAuditEvent(tenantId, "system_changed", "group_created"),
            NewAuditEvent(Guid.NewGuid(), "system_changed", "other_tenant"));

        var response = await client.GetFromJsonAsync<List<AuditEventResponse>>(
            $"/api/admin/audit?tenantId={tenantId}&eventType=");

        response.Should().NotBeNull();
        response!.Select(auditEvent => auditEvent.EventType).Should().BeEquivalentTo(
            "permission_changed",
            "system_changed");
        response.Should().OnlyContain(auditEvent => auditEvent.TenantId == tenantId);
    }

    [Fact]
    public async Task Admin_audit_csv_filters_by_tenant_and_event_type_and_escapes_fields()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedAuditEventsAsync(
            NewAuditEvent(tenantId, "permission_changed", "comma,quote\"cr\r\nlf", fileId, userId),
            NewAuditEvent(tenantId, "system_changed", "group_created"),
            NewAuditEvent(Guid.NewGuid(), "permission_changed", "other_tenant"));

        using var response = await client.GetAsync($"/api/admin/audit.csv?tenantId={tenantId}&eventType=permission_changed");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().StartWith("createdAtUtc,tenantId,fileId,userId,eventType,reasonCode\r\n");
        csv.Should().Contain($",{tenantId},{fileId},{userId},permission_changed,");
        csv.Should().Contain("\"comma,quote\"\"cr\r\nlf\"");
        csv.Should().NotContain("other_tenant");
        csv.Should().NotContain("group_created");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private async Task SeedAuditEventsAsync(params AuditEventEntity[] auditEvents)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.AuditEvents.AddRange(auditEvents);
        await dbContext.SaveChangesAsync();
    }

    private static AuditEventEntity NewAuditEvent(
        Guid tenantId,
        string eventType,
        string reasonCode,
        Guid? fileId = null,
        Guid? userId = null)
    {
        return new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = eventType,
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
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
    public async Task Audit_json_with_mismatched_header_returns_400_tenant_mismatch()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit?tenantId={tenantId}");
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    [Fact]
    public async Task Audit_csv_with_mismatched_header_returns_400_tenant_mismatch()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/audit.csv?tenantId={tenantId}");
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record ErrorBody(string ReasonCode);
}
