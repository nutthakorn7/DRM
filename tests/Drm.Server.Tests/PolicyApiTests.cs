using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class PolicyApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-server-tests-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public PolicyApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Registering_file_allows_owner_to_view()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        var fileId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            fileId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View",
            "user:{userId}"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var registeredFile = await registerResponse.Content.ReadFromJsonAsync<RegisterFileResponse>();
        registeredFile.Should().NotBeNull();
        registeredFile!.FileId.Should().Be(fileId);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            fileId,
            ownerUserId,
            Guid.NewGuid(),
            "View",
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    [Fact]
    public async Task Registering_task5_file_shape_uses_default_watermark()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View"
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            fileId,
            ownerUserId,
            Guid.NewGuid(),
            "View",
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View",
            ReasonCode = "allowed",
            WatermarkTemplate = "{user} {time} {file}"
        });
    }

    [Fact]
    public async Task Deciding_task5_policy_shape_for_expired_file_denies_expired()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            permissions = "View"
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "expired",
            WatermarkTemplate = (string?)null
        });
    }

    [Fact]
    public async Task Revoking_file_denies_future_owner_view_decisions()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        var fileId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            fileId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View",
            "user:{userId}"));
        var registeredFile = await registerResponse.Content.ReadFromJsonAsync<RegisterFileResponse>();

        using var revokeResponse = await client.PostAsync($"/api/files/{registeredFile!.FileId}/revoke?tenantId={tenantId}", content: null);

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            registeredFile.FileId,
            ownerUserId,
            Guid.NewGuid(),
            "View",
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "revoked",
            WatermarkTemplate = (string?)null
        });
    }

    [Fact]
    public async Task Registering_duplicate_tenant_file_returns_conflict()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var request = new RegisterFileRequest(
            tenantId,
            fileId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View, Print",
            "user:{userId}");

        using var firstResponse = await client.PostAsJsonAsync("/api/files", request);
        using var duplicateResponse = await client.PostAsJsonAsync("/api/files", request);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Registering_file_with_invalid_permissions_returns_bad_request()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View, Fly",
            "user:{userId}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deciding_policy_with_invalid_requested_permission_returns_bad_request()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Fly",
            DateTimeOffset.UtcNow));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoking_with_wrong_tenant_returns_not_found_and_does_not_revoke_actual_file()
    {
        using var client = factory.CreateClient();
        var actualTenantId = Guid.NewGuid();
        var wrongTenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            actualTenantId,
            fileId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View",
            "user:{userId}"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var wrongTenantRevokeResponse = await client.PostAsync($"/api/files/{fileId}/revoke?tenantId={wrongTenantId}", content: null);

        wrongTenantRevokeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            actualTenantId,
            fileId,
            ownerUserId,
            Guid.NewGuid(),
            "View",
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    public void Dispose()
    {
        factory.Dispose();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private sealed record RegisterFileRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string WatermarkTemplate);

    private sealed record RegisterFileResponse(Guid FileId);

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission,
        DateTimeOffset AtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate);
}
