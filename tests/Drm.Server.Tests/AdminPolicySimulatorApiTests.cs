using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AdminPolicySimulatorApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-policy-simulator-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminPolicySimulatorApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_policy_simulator_returns_decision_without_writing_access_audit()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "user:{userId}"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var auditCountBefore = await CountAuditEventsAsync();

        using var simulateResponse = await client.PostAsJsonAsync("/api/admin/policy-simulator", new
        {
            tenantId,
            fileId,
            userId = ownerUserId,
            deviceId,
            requestedPermission = "View"
        });

        simulateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await simulateResponse.Content.ReadFromJsonAsync<PolicySimulationResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}",
            Simulated = true
        });
        decision!.OfflineLeaseExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);

        var auditCountAfter = await CountAuditEventsAsync();
        auditCountAfter.Should().Be(auditCountBefore);
    }

    [Fact]
    public async Task Admin_policy_simulator_returns_bad_request_for_invalid_permission()
    {
        using var client = factory.CreateClient();

        using var simulateResponse = await client.PostAsJsonAsync("/api/admin/policy-simulator", new
        {
            tenantId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            requestedPermission = "Fly"
        });

        simulateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var decision = await simulateResponse.Content.ReadFromJsonAsync<PolicySimulationResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "invalid_permissions",
            Simulated = true
        });
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private async Task<int> CountAuditEventsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.AuditEvents.CountAsync();
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

    private sealed record PolicySimulationResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc,
        bool Simulated);
}
