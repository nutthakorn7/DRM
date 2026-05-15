using System.Net;
using System.Net.Http.Json;
using Drm.Domain;
using Drm.Server;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        decision!.OfflineLeaseExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
        decision.OfflineLeaseExpiresAtUtc.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Registering_file_creates_owner_file_grant()
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
            "View, Print",
            "user:{userId}"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grant = await dbContext.FileGrants.AsNoTracking().SingleAsync();

        grant.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            FileId = fileId,
            SubjectType = "User",
            SubjectId = ownerUserId,
            Permissions = "View, Print"
        });
        grant.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Registering_file_with_policy_template_and_recipients_applies_template_policy()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var directRecipientUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var groupMemberUserId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var createGroup = await client.PostAsJsonAsync("/api/admin/groups", new
        {
            tenantId,
            groupId,
            name = "Legal"
        });
        using var addMember = await client.PostAsJsonAsync($"/api/admin/groups/{groupId}/members", new
        {
            tenantId,
            userId = groupMemberUserId
        });
        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Restricted",
            permissions = "View, Print",
            watermarkTemplate = "restricted:{userId}:{fileId}",
            offlineLeaseMinutes = 15,
            allowPrint = true
        });
        createGroup.StatusCode.Should().Be(HttpStatusCode.Created);
        addMember.StatusCode.Should().Be(HttpStatusCode.Created);
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "old:{userId}",
            policyTemplateId = templateId,
            recipients = new[]
            {
                new { subjectType = "User", subjectId = directRecipientUserId },
                new { subjectType = "Group", subjectId = groupId }
            }
        });

        register.StatusCode.Should().Be(HttpStatusCode.Created);
        var registered = await register.Content.ReadFromJsonAsync<RegisterFileResponse>();
        registered.Should().BeEquivalentTo(new
        {
            FileId = fileId,
            TenantId = tenantId,
            OwnerUserId = ownerUserId,
            ContentType = "application/pdf",
            Permissions = "View, Print",
            WatermarkTemplate = "restricted:{userId}:{fileId}"
        });

        var ownerDecision = await DecideAsync(client, tenantId, fileId, ownerUserId, "Print");
        var directRecipientDecision = await DecideAsync(client, tenantId, fileId, directRecipientUserId, "Print");
        var groupMemberDecision = await DecideAsync(client, tenantId, fileId, groupMemberUserId, "Print");

        ownerDecision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "restricted:{userId}:{fileId}"
        });
        directRecipientDecision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "restricted:{userId}:{fileId}"
        });
        groupMemberDecision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "restricted:{userId}:{fileId}"
        });
    }

    [Fact]
    public async Task Template_offline_lease_minutes_controls_allowed_decision_lease()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Offline 45",
            permissions = "View",
            watermarkTemplate = "offline:{userId}",
            offlineLeaseMinutes = 45,
            allowPrint = false
        });
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            policyTemplateId = templateId
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var requestedAt = DateTimeOffset.UtcNow;
        var decision = await DecideAsync(client, tenantId, fileId, ownerUserId, "View");

        decision.Allowed.Should().BeTrue();
        decision.OfflineLeaseExpiresAtUtc.Should().BeCloseTo(requestedAt.AddMinutes(45), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Template_offline_lease_zero_disables_allowed_decision_offline_lease()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Online only",
            permissions = "View",
            watermarkTemplate = "online:{userId}",
            offlineLeaseMinutes = 0,
            allowPrint = false
        });
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            policyTemplateId = templateId
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var decision = await DecideAsync(client, tenantId, fileId, ownerUserId, "View");

        decision.Allowed.Should().BeTrue();
        decision.OfflineLeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Registering_file_with_missing_template_or_group_recipient_returns_not_found()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var missingTemplate = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId = Guid.NewGuid(),
            ownerUserId = Guid.NewGuid(),
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            policyTemplateId = Guid.NewGuid()
        });

        missingTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var templateId = Guid.NewGuid();
        using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Restricted",
            permissions = "View, Print",
            watermarkTemplate = "restricted:{userId}:{fileId}",
            offlineLeaseMinutes = 15,
            allowPrint = true
        });
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var missingGroup = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId = Guid.NewGuid(),
            ownerUserId = Guid.NewGuid(),
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            policyTemplateId = templateId,
            recipients = new[]
            {
                new { subjectType = "Group", subjectId = Guid.NewGuid() }
            }
        });

        missingGroup.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Existing_file_without_file_grant_still_allows_owner_from_legacy_file_policy()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.ProtectedFiles.Add(new ProtectedFileEntity
            {
                Id = fileId,
                TenantId = tenantId,
                OwnerUserId = ownerUserId,
                ContentType = "application/pdf",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                Revoked = false,
                Permissions = Permission.View,
                WatermarkTemplate = "user:{userId}"
            });
            await dbContext.SaveChangesAsync();
        }

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
    public async Task Legacy_owner_permissions_still_apply_when_owner_also_matches_group_grant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.ProtectedFiles.Add(new ProtectedFileEntity
            {
                Id = fileId,
                TenantId = tenantId,
                OwnerUserId = ownerUserId,
                ContentType = "application/pdf",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                Revoked = false,
                Permissions = Permission.View,
                WatermarkTemplate = "user:{userId}"
            });
            dbContext.TenantGroups.Add(new TenantGroupEntity
            {
                TenantId = tenantId,
                GroupId = groupId,
                Name = "Legal",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.GroupMembers.Add(new GroupMemberEntity
            {
                TenantId = tenantId,
                GroupId = groupId,
                UserId = ownerUserId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.FileGrants.Add(new FileGrantEntity
            {
                TenantId = tenantId,
                FileId = fileId,
                SubjectType = "Group",
                SubjectId = groupId,
                Permissions = "Print",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

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
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    [Fact]
    public async Task User_without_effective_grant_cannot_get_allowed_decision_for_none_permission()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            fileId,
            Guid.NewGuid(),
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View",
            "user:{userId}"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            fileId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "None",
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "no_grant",
            WatermarkTemplate = (string?)null
        });
        decision!.OfflineLeaseExpiresAtUtc.Should().BeNull();
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
    public async Task Deciding_expired_file_uses_server_time_not_client_supplied_time()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            fileId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "View",
            "user:{userId}"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            fileId,
            ownerUserId,
            Guid.NewGuid(),
            "View",
            DateTimeOffset.UtcNow.AddDays(-30)));

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

    [Fact]
    public async Task Deciding_policy_for_disabled_device_denies_with_device_disabled_reason()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var registerFileResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
            tenantId,
            fileId,
            ownerUserId,
            "application/pdf",
            DateTimeOffset.UtcNow.AddHours(1),
            "View",
            "user:{userId}"));
        registerFileResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await RegisterDeviceAsync(client, tenantId, ownerUserId, deviceId);
        await DisableDeviceAsync(client, tenantId, deviceId);

        using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            fileId,
            ownerUserId,
            deviceId,
            "View",
            DateTimeOffset.UtcNow));

        decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "device_disabled",
            WatermarkTemplate = (string?)null,
            OfflineLeaseExpiresAtUtc = (DateTimeOffset?)null
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

    private sealed record RegisterFileResponse(
        Guid FileId,
        Guid TenantId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string WatermarkTemplate);

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
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);

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

    private static async Task<PolicyDecisionResponse> DecideAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid userId,
        string requestedPermission)
    {
        using var response = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
            tenantId,
            fileId,
            userId,
            Guid.NewGuid(),
            requestedPermission,
            DateTimeOffset.UtcNow));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<PolicyDecisionResponse>()
            ?? throw new InvalidOperationException("Policy decision response was empty.");
    }
}
