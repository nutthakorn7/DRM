using System.Net;
using System.Net.Http.Json;
using Drm.Domain;
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

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            Permission.View,
            "user:{userId}"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var registeredFile = await registerResponse.Content.ReadFromJsonAsync<RegisterFileResponse>();
        registeredFile.Should().NotBeNull();
        registeredFile!.FileId.Should().NotBeEmpty();

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            registeredFile.FileId,
            ownerUserId,
            Guid.NewGuid(),
            Permission.View,
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = Permission.View,
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    [Fact]
    public async Task Revoking_file_denies_future_owner_view_decisions()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            Permission.View,
            "user:{userId}"));
        var registeredFile = await registerResponse.Content.ReadFromJsonAsync<RegisterFileResponse>();

        using var revokeResponse = await client.PostAsync($"/api/files/{registeredFile!.FileId}/revoke", content: null);

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            registeredFile.FileId,
            ownerUserId,
            Guid.NewGuid(),
            Permission.View,
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = Permission.None,
            ReasonCode = "revoked",
            WatermarkTemplate = (string?)null
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
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        Permission Permissions,
        string WatermarkTemplate);

    private sealed record RegisterFileResponse(Guid FileId);

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        Permission RequestedPermission,
        DateTimeOffset AtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        Permission AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate);
}
