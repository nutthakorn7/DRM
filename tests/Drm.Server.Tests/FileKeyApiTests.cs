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
    public async Task Max_opens_template_caps_per_user_access_and_returns_403_after_limit()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var fileKey = EnvelopeCrypto.GenerateKey();

        // Template with MaxOpens=3 — the owner gets exactly three opens.
        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Three-open cap",
            permissions = "View",
            watermarkTemplate = "user:{userId}",
            offlineLeaseMinutes = 15,
            allowPrint = false,
            maxOpens = 3
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

        // Open #1 — allowed, 2 remaining after consumption.
        var first = await UnwrapAsync(client, tenantId, fileId, ownerUserId);
        first.statusCode.Should().Be(HttpStatusCode.OK);
        first.body!.MaxOpens.Should().Be(3);
        first.body.OpensRemaining.Should().Be(2);

        // Open #2 — allowed, 1 remaining.
        var second = await UnwrapAsync(client, tenantId, fileId, ownerUserId);
        second.statusCode.Should().Be(HttpStatusCode.OK);
        second.body!.OpensRemaining.Should().Be(1);

        // Open #3 — allowed, 0 remaining (last open consumed).
        var third = await UnwrapAsync(client, tenantId, fileId, ownerUserId);
        third.statusCode.Should().Be(HttpStatusCode.OK);
        third.body!.OpensRemaining.Should().Be(0);

        // Open #4 — denied with opens_exhausted. The endpoint returns 403
        // for any policy denial; the reason code lives in the error body.
        using var fourth = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });
        fourth.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await fourth.Content.ReadFromJsonAsync<ErrorBody>();
        error!.ReasonCode.Should().Be("opens_exhausted");
    }

    [Fact]
    public async Task Max_opens_is_tracked_independently_per_user()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var fileKey = EnvelopeCrypto.GenerateKey();

        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "One-open cap",
            permissions = "View",
            watermarkTemplate = "user:{userId}",
            offlineLeaseMinutes = 15,
            allowPrint = false,
            maxOpens = 1
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

        // Grant the second user too so the test isn't conflated with no_grant.
        using var grantResponse = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            adminUserId = Guid.NewGuid(),
            subjectType = "User",
            subjectId = secondUserId,
            permissions = "View"
        });
        grantResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Owner burns their single open.
        var ownerOpen = await UnwrapAsync(client, tenantId, fileId, ownerUserId);
        ownerOpen.statusCode.Should().Be(HttpStatusCode.OK);

        // Owner attempt #2 is denied — they have no opens left.
        using var ownerSecondAttempt = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });
        ownerSecondAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Second user has their OWN counter and can still open the file.
        var secondUserOpen = await UnwrapAsync(client, tenantId, fileId, secondUserId);
        secondUserOpen.statusCode.Should().Be(HttpStatusCode.OK);
        secondUserOpen.body!.OpensRemaining.Should().Be(0);
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

    private static async Task<(HttpStatusCode statusCode, UnwrapFileKeyResponse? body)> UnwrapAsync(
        HttpClient client, Guid tenantId, Guid fileId, Guid userId)
    {
        using var response = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });
        var body = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<UnwrapFileKeyResponse>()
            : null;
        return (response.StatusCode, body);
    }

    private sealed record ErrorBody(string ReasonCode);

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
        DateTimeOffset? OfflineLeaseExpiresAtUtc,
        int? MaxOpens,
        int? OpensRemaining);
}
