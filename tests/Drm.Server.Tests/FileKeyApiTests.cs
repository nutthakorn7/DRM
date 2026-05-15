using System.Net;
using System.Net.Http.Json;
using Drm.Crypto;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class FileKeyApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-file-key-api-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public FileKeyApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:KeyWrapping:MasterKeyBase64", Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()));
            });
    }

    [Fact]
    public async Task Allowed_owner_can_wrap_and_unwrap_file_key()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fileKey = EnvelopeCrypto.GenerateKey();
        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);

        using var wrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/wrap", new
        {
            tenantId,
            fileKeyBase64 = Convert.ToBase64String(fileKey)
        });

        wrapResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var requestedAt = DateTimeOffset.UtcNow;
        using var unwrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId,
            requestedPermission = "View"
        });

        unwrapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unwrapped = await unwrapResponse.Content.ReadFromJsonAsync<UnwrapFileKeyResponse>();
        Convert.FromBase64String(unwrapped!.FileKeyBase64).Should().Equal(fileKey);
        unwrapped.AllowedPermissions.Should().Be("View");
        unwrapped.WatermarkTemplate.Should().Be("user:{userId}");
        unwrapped.OfflineLeaseExpiresAtUtc.Should().BeAfter(requestedAt);
    }

    [Fact]
    public async Task Template_offline_lease_minutes_flow_to_unwrap_response()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var fileKey = EnvelopeCrypto.GenerateKey();

        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Offline 30",
            permissions = "View",
            watermarkTemplate = "user:{userId}",
            offlineLeaseMinutes = 30,
            allowPrint = false
        });
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            policyTemplateId = templateId
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await WrapKeyAsync(client, tenantId, fileId, fileKey);

        var requestedAt = DateTimeOffset.UtcNow;
        using var unwrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        unwrapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unwrapped = await unwrapResponse.Content.ReadFromJsonAsync<UnwrapFileKeyResponse>();
        unwrapped!.OfflineLeaseExpiresAtUtc.Should().BeCloseTo(requestedAt.AddMinutes(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Unwrap_denies_user_without_policy_grant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);
        await WrapKeyAsync(client, tenantId, fileId, EnvelopeCrypto.GenerateKey());

        using var unwrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        unwrapResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unwrap_denies_disabled_device_even_when_user_has_policy_grant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);
        await WrapKeyAsync(client, tenantId, fileId, EnvelopeCrypto.GenerateKey());
        await RegisterDeviceAsync(client, tenantId, ownerUserId, deviceId);
        await DisableDeviceAsync(client, tenantId, deviceId);

        using var unwrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId,
            requestedPermission = "View"
        });

        unwrapResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unwrap_missing_wrapped_key_returns_not_found()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);

        using var unwrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        unwrapResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Wrapping_same_file_replaces_previous_key()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var firstKey = EnvelopeCrypto.GenerateKey();
        var secondKey = EnvelopeCrypto.GenerateKey();
        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);
        await WrapKeyAsync(client, tenantId, fileId, firstKey);
        await WrapKeyAsync(client, tenantId, fileId, secondKey);

        using var unwrapResponse = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        unwrapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unwrapped = await unwrapResponse.Content.ReadFromJsonAsync<UnwrapFileKeyResponse>();
        Convert.FromBase64String(unwrapped!.FileKeyBase64).Should().Equal(secondKey);
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static async Task RegisterFileAsync(HttpClient client, Guid tenantId, Guid ownerUserId, Guid fileId)
    {
        using var response = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "user:{userId}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task WrapKeyAsync(HttpClient client, Guid tenantId, Guid fileId, byte[] fileKey)
    {
        using var response = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/wrap", new
        {
            tenantId,
            fileKeyBase64 = Convert.ToBase64String(fileKey)
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
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

    private static async Task DisableDeviceAsync(HttpClient client, Guid tenantId, Guid deviceId)
    {
        using var response = await client.PostAsJsonAsync($"/api/admin/devices/{deviceId}/disable", new
        {
            tenantId,
            adminUserId = Guid.NewGuid(),
            reason = "admin_disabled"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    private sealed record UnwrapFileKeyResponse(
        Guid TenantId,
        Guid FileId,
        string FileKeyBase64,
        string AllowedPermissions,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);
}
