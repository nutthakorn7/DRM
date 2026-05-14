using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AgentCommandApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-agent-command-api-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AgentCommandApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_enqueue_delete_command_and_agent_can_complete_it()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await RegisterDeviceAsync(client, tenantId, userId, deviceId);
        await RegisterFileAsync(client, tenantId, userId, fileId);

        using var enqueueResponse = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/commands/delete-protected-copy",
            new
            {
                tenantId,
                deviceId,
                adminUserId = userId
            });

        enqueueResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await enqueueResponse.Content.ReadFromJsonAsync<AgentCommandResponse>();
        created.Should().NotBeNull();
        created!.TenantId.Should().Be(tenantId);
        created.DeviceId.Should().Be(deviceId);
        created.FileId.Should().Be(fileId);
        created.CommandType.Should().Be("DeleteProtectedCopy");
        created.Status.Should().Be("Pending");

        var pending = await client.GetFromJsonAsync<List<AgentCommandResponse>>(
            $"/api/agent/devices/{deviceId}/commands?tenantId={tenantId}");
        pending.Should().ContainSingle(command => command.CommandId == created.CommandId);

        using var completeResponse = await client.PostAsJsonAsync(
            $"/api/agent/devices/{deviceId}/commands/{created.CommandId}/complete",
            new
            {
                tenantId,
                status = "Completed",
                reasonCode = "deleted"
            });

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completed = await completeResponse.Content.ReadFromJsonAsync<AgentCommandResponse>();
        completed.Should().NotBeNull();
        completed!.Status.Should().Be("Completed");
        completed.ReasonCode.Should().Be("deleted");
        completed.CompletedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var pendingAfterComplete = await client.GetFromJsonAsync<List<AgentCommandResponse>>(
            $"/api/agent/devices/{deviceId}/commands?tenantId={tenantId}");
        pendingAfterComplete.Should().BeEmpty();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditTypes = await dbContext.AuditEvents
            .AsNoTracking()
            .Select(audit => audit.EventType)
            .ToListAsync();
        auditTypes.Should().Contain("protected_file_delete_requested");
        auditTypes.Should().Contain("protected_file_delete_completed");
    }

    [Fact]
    public async Task Enqueue_delete_command_requires_file_and_device_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await RegisterDeviceAsync(client, tenantId, userId, deviceId);
        await RegisterFileAsync(client, tenantId, userId, fileId);

        using var wrongTenantResponse = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/commands/delete-protected-copy",
            new
            {
                tenantId = otherTenantId,
                deviceId,
                adminUserId = userId
            });

        using var unknownDeviceResponse = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/commands/delete-protected-copy",
            new
            {
                tenantId,
                deviceId = Guid.NewGuid(),
                adminUserId = userId
            });

        wrongTenantResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownDeviceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Completing_command_with_wrong_device_returns_not_found()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await RegisterDeviceAsync(client, tenantId, userId, deviceId);
        await RegisterFileAsync(client, tenantId, userId, fileId);
        using var enqueueResponse = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/commands/delete-protected-copy",
            new
            {
                tenantId,
                deviceId,
                adminUserId = userId
            });
        var created = await enqueueResponse.Content.ReadFromJsonAsync<AgentCommandResponse>();

        using var completeResponse = await client.PostAsJsonAsync(
            $"/api/agent/devices/{Guid.NewGuid()}/commands/{created!.CommandId}/complete",
            new
            {
                tenantId,
                status = "Completed",
                reasonCode = "deleted"
            });

        completeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static async Task RegisterDeviceAsync(HttpClient client, Guid tenantId, Guid userId, Guid deviceId)
    {
        using var response = await client.PostAsJsonAsync("/api/agent/devices/register", new
        {
            tenantId,
            userId,
            deviceId,
            hostname = "WIN-001",
            operatingSystem = "Windows 11",
            agentVersion = "0.1.0"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task RegisterFileAsync(HttpClient client, Guid tenantId, Guid userId, Guid fileId)
    {
        using var response = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId = userId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "user:{userId}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private sealed record AgentCommandResponse(
        Guid TenantId,
        Guid CommandId,
        Guid DeviceId,
        Guid FileId,
        string CommandType,
        string Status,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc);
}
