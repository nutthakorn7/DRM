using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AdminFilesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-files-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminFilesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Group_grant_allows_group_member_to_view_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId, permissions: "Print");
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var createGroup = await CreateGroupAsync(client, tenantId, groupId);
        createGroup.StatusCode.Should().Be(HttpStatusCode.Created);

        using var addMember = await AddMemberAsync(client, tenantId, groupId, memberUserId);
        addMember.StatusCode.Should().Be(HttpStatusCode.Created);

        using var grant = await UpsertGrantAsync(client, tenantId, fileId, "group", groupId, "view");
        grant.StatusCode.Should().Be(HttpStatusCode.Created);

        using var decide = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = memberUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        decide.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decide.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    [Fact]
    public async Task Admin_upsert_file_grant_rejects_invalid_subject_type()
    {
        using var client = factory.CreateClient();

        using var response = await UpsertGrantAsync(
            client,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Role",
            Guid.NewGuid(),
            "View");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse("invalid_subject_type"));
    }

    [Fact]
    public async Task Admin_upsert_file_grant_rejects_invalid_permissions()
    {
        using var client = factory.CreateClient();

        using var response = await UpsertGrantAsync(
            client,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "User",
            Guid.NewGuid(),
            "Fly");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse("invalid_permissions"));
    }

    [Fact]
    public async Task Admin_upsert_file_grant_returns_not_found_for_missing_file()
    {
        using var client = factory.CreateClient();

        using var response = await UpsertGrantAsync(
            client,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "User",
            Guid.NewGuid(),
            "View");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_upsert_file_grant_returns_not_found_for_missing_group()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var response = await UpsertGrantAsync(
            client,
            tenantId,
            fileId,
            "Group",
            Guid.NewGuid(),
            "View");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_upsert_file_grant_updates_existing_permissions()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var first = await UpsertGrantAsync(client, tenantId, fileId, "user", userId, "view");
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await UpsertGrantAsync(client, tenantId, fileId, "USER", userId, "view, print");
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var grant = await second.Content.ReadFromJsonAsync<FileGrantResponse>();
        grant.Should().BeEquivalentTo(new FileGrantResponse(
            tenantId,
            fileId,
            "User",
            userId,
            "View, Print"));

        using var decide = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "Print"
        });

        decide.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await decide.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    [Fact]
    public async Task Admin_list_files_is_tenant_scoped_ordered_and_filters_by_content_type()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var lowFileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var highFileId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var secondTenantFileId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        using var high = await RegisterFileAsync(client, tenantId, highFileId, Guid.NewGuid(), contentType: "application/pdf");
        using var low = await RegisterFileAsync(client, tenantId, lowFileId, Guid.NewGuid(), contentType: "application/pdf");
        using var otherContentType = await RegisterFileAsync(client, tenantId, Guid.NewGuid(), Guid.NewGuid(), contentType: "text/plain");
        using var otherTenant = await RegisterFileAsync(client, secondTenantId, secondTenantFileId, Guid.NewGuid(), contentType: "application/pdf");
        high.StatusCode.Should().Be(HttpStatusCode.Created);
        low.StatusCode.Should().Be(HttpStatusCode.Created);
        otherContentType.StatusCode.Should().Be(HttpStatusCode.Created);
        otherTenant.StatusCode.Should().Be(HttpStatusCode.Created);

        var files = await client.GetFromJsonAsync<List<FileResponse>>(
            $"/api/admin/files?tenantId={tenantId}&q=pdf");

        files.Should().NotBeNull();
        files!.Select(file => file.FileId).Should().Equal(lowFileId, highFileId);
        files.Should().OnlyContain(file => file.TenantId == tenantId && file.ContentType == "application/pdf");
    }

    [Fact]
    public async Task Admin_can_apply_policy_template_to_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId, permissions: "View");
        using var createTemplate = await CreatePolicyTemplateAsync(
            client,
            tenantId,
            templateId,
            "Restricted",
            "View, Print",
            "restricted:{userId}:{fileId}");
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var apply = await ApplyPolicyTemplateAsync(client, tenantId, fileId, templateId, adminUserId);

        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = await apply.Content.ReadFromJsonAsync<FileResponse>();
        applied.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            FileId = fileId,
            OwnerUserId = ownerUserId,
            ContentType = "application/pdf",
            Permissions = "View, Print",
            WatermarkTemplate = "restricted:{userId}:{fileId}",
            Revoked = false
        });

        using var decide = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "Print"
        });
        var policyDecision = await decide.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        policyDecision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "restricted:{userId}:{fileId}"
        });

        var files = await client.GetFromJsonAsync<List<FileResponse>>($"/api/admin/files?tenantId={tenantId}&q=");
        files.Should().ContainSingle(file =>
            file.FileId == fileId &&
            file.Permissions == "View, Print" &&
            file.WatermarkTemplate == "restricted:{userId}:{fileId}");

        using var auditResponse = await client.GetAsync($"/api/audit?tenantId={tenantId}");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<AuditEventResponse>>();
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "permission_changed" &&
            auditEvent.ReasonCode == "policy_template_applied" &&
            auditEvent.FileId == fileId &&
            auditEvent.UserId == adminUserId);
    }

    [Fact]
    public async Task Apply_policy_template_updates_offline_lease_duration()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId, permissions: "View");
        using var createTemplate = await CreatePolicyTemplateAsync(
            client,
            tenantId,
            templateId,
            "Extended offline",
            "View",
            "extended:{userId}",
            offlineLeaseMinutes: 45);
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var apply = await ApplyPolicyTemplateAsync(client, tenantId, fileId, templateId, Guid.NewGuid());
        apply.StatusCode.Should().Be(HttpStatusCode.OK);

        var requestedAt = DateTimeOffset.UtcNow;
        using var decide = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        decide.StatusCode.Should().Be(HttpStatusCode.OK);
        var policyDecision = await decide.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        policyDecision!.Allowed.Should().BeTrue();
        policyDecision.OfflineLeaseExpiresAtUtc.Should().BeCloseTo(
            requestedAt.AddMinutes(45),
            TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Admin_apply_policy_template_returns_not_found_for_missing_file_or_template()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var otherTenantTemplateId = Guid.NewGuid();

        using var missingFile = await ApplyPolicyTemplateAsync(
            client,
            tenantId,
            fileId,
            templateId,
            Guid.NewGuid());
        missingFile.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var missingTemplate = await ApplyPolicyTemplateAsync(
            client,
            tenantId,
            fileId,
            templateId,
            Guid.NewGuid());
        missingTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var createOtherTenantTemplate = await CreatePolicyTemplateAsync(
            client,
            otherTenantId,
            otherTenantTemplateId,
            "Other tenant",
            "View, Print",
            "other:{userId}");
        createOtherTenantTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var crossTenantTemplate = await ApplyPolicyTemplateAsync(
            client,
            tenantId,
            fileId,
            otherTenantTemplateId,
            Guid.NewGuid());
        crossTenantTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_revoke_file_and_policy_denies_future_access()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId);
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var revoke = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/revoke", new
        {
            tenantId,
            adminUserId
        });

        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        var revoked = await revoke.Content.ReadFromJsonAsync<RevokeFileResponse>();
        revoked.Should().BeEquivalentTo(new RevokeFileResponse(tenantId, fileId, true));

        var files = await client.GetFromJsonAsync<List<FileResponse>>($"/api/admin/files?tenantId={tenantId}&q=");
        files.Should().ContainSingle(file => file.FileId == fileId && file.Revoked);

        using var decide = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = ownerUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        var decision = await decide.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "revoked",
            WatermarkTemplate = (string?)null
        });
    }

    [Fact]
    public async Task Admin_revoke_with_wrong_tenant_returns_not_found_and_does_not_revoke_actual_file()
    {
        using var client = factory.CreateClient();
        var actualTenantId = Guid.NewGuid();
        var wrongTenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, actualTenantId, fileId, ownerUserId);
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var revoke = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/revoke", new
        {
            tenantId = wrongTenantId,
            adminUserId = Guid.NewGuid()
        });

        revoke.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var files = await client.GetFromJsonAsync<List<FileResponse>>($"/api/admin/files?tenantId={actualTenantId}&q=");
        files.Should().ContainSingle(file => file.FileId == fileId && !file.Revoked);
    }

    [Fact]
    public async Task Admin_can_bulk_replace_file_grants()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, firstUserId);
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var replace = await ReplaceGrantsAsync(client, tenantId, fileId, new[]
        {
            new GrantRequest("User", secondUserId, "View, Print")
        });

        replace.StatusCode.Should().Be(HttpStatusCode.OK);

        var replacedGrants = await replace.Content.ReadFromJsonAsync<List<FileGrantResponse>>();
        replacedGrants.Should().BeEquivalentTo([
            new FileGrantResponse(tenantId, fileId, "User", secondUserId, "View, Print")
        ]);

        using var firstDecision = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = firstUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });
        using var secondDecision = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = secondUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "Print"
        });

        var firstPolicyDecision = await firstDecision.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        var secondPolicyDecision = await secondDecision.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        firstPolicyDecision.Should().BeEquivalentTo(new
        {
            Allowed = false,
            AllowedPermissions = "None",
            ReasonCode = "no_grant",
            WatermarkTemplate = (string?)null
        });
        secondPolicyDecision.Should().BeEquivalentTo(new
        {
            Allowed = true,
            AllowedPermissions = "View, Print",
            ReasonCode = "allowed",
            WatermarkTemplate = "user:{userId}"
        });
    }

    [Fact]
    public async Task Admin_bulk_replace_file_grants_validates_items_before_replacing()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var originalUserId = Guid.NewGuid();
        var replacementUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, originalUserId);
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicateReplace = await ReplaceGrantsAsync(client, tenantId, fileId, new[]
        {
            new GrantRequest("User", replacementUserId, "View"),
            new GrantRequest("user", replacementUserId, "Print")
        });

        duplicateReplace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var duplicateError = await duplicateReplace.Content.ReadFromJsonAsync<ErrorResponse>();
        duplicateError.Should().BeEquivalentTo(new ErrorResponse("duplicate_grant"));

        using var invalidPermissionReplace = await ReplaceGrantsAsync(client, tenantId, fileId, new[]
        {
            new GrantRequest("User", replacementUserId, "Fly")
        });

        invalidPermissionReplace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var permissionError = await invalidPermissionReplace.Content.ReadFromJsonAsync<ErrorResponse>();
        permissionError.Should().BeEquivalentTo(new ErrorResponse("invalid_permissions"));

        using var originalDecision = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = originalUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        var decision = await originalDecision.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_bulk_replace_file_grants_rejects_null_or_missing_grants()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var nullGrants = await client.PutAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            grants = (object?)null
        });

        nullGrants.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var nullError = await nullGrants.Content.ReadFromJsonAsync<ErrorResponse>();
        nullError.Should().BeEquivalentTo(new ErrorResponse("invalid_grants"));

        using var missingGrants = await client.PutAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId
        });

        missingGrants.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var missingError = await missingGrants.Content.ReadFromJsonAsync<ErrorResponse>();
        missingError.Should().BeEquivalentTo(new ErrorResponse("invalid_grants"));

        using var nullItem = await client.PutAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            grants = new object?[] { null }
        });

        nullItem.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var nullItemError = await nullItem.Content.ReadFromJsonAsync<ErrorResponse>();
        nullItemError.Should().BeEquivalentTo(new ErrorResponse("invalid_grants"));
    }

    [Fact]
    public async Task Admin_bulk_replace_file_grants_returns_not_found_for_missing_file_or_group()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var missingFile = await ReplaceGrantsAsync(client, tenantId, fileId, new[]
        {
            new GrantRequest("User", Guid.NewGuid(), "View")
        });

        missingFile.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var missingGroup = await ReplaceGrantsAsync(client, tenantId, fileId, new[]
        {
            new GrantRequest("Group", Guid.NewGuid(), "View")
        });

        missingGroup.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_create_list_and_revoke_external_share_links()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId);
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var create = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "external.user@example.com",
            expiresAtUtc,
            maxUses: 1);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();
        created.Should().NotBeNull();
        created!.TenantId.Should().Be(tenantId);
        created.FileId.Should().Be(fileId);
        created.GuestEmail.Should().Be("external.user@example.com");
        created.MaxUses.Should().Be(1);
        created.UsedCount.Should().Be(0);
        created.Revoked.Should().BeFalse();
        created.AccessToken.Should().NotBeNullOrWhiteSpace();
        created.AccessToken.Length.Should().BeGreaterThan(30);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await dbContext.ExternalShareLinks.AsNoTracking().SingleAsync();
            stored.TokenHash.Should().NotBe(created.AccessToken);
            stored.TokenHash.Should().NotBeNullOrWhiteSpace();
        }

        using var listResponse = await client.GetAsync($"/api/admin/files/{fileId}/share-links?tenantId={tenantId}");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listJson.Should().NotContain(created.AccessToken);
        listJson.ToLowerInvariant().Should().NotContain("tokenhash");

        var links = await listResponse.Content.ReadFromJsonAsync<List<ExternalShareLinkResponse>>();
        links.Should().BeEquivalentTo([
            new ExternalShareLinkResponse(
                tenantId,
                created.ShareLinkId,
                fileId,
                "external.user@example.com",
                created.ExpiresAtUtc,
                1,
                0,
                false,
                created.CreatedAtUtc,
                null)
        ], options => options
            .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(2)))
            .WhenTypeIs<DateTimeOffset>());

        using var revoke = await RevokeShareLinkAsync(client, tenantId, fileId, created.ShareLinkId, adminUserId);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        var revoked = await revoke.Content.ReadFromJsonAsync<ExternalShareLinkResponse>();
        revoked!.Revoked.Should().BeTrue();
        revoked.RevokedAtUtc.Should().NotBeNull();

        var afterRevoke = await client.GetFromJsonAsync<List<ExternalShareLinkResponse>>(
            $"/api/admin/files/{fileId}/share-links?tenantId={tenantId}");
        afterRevoke.Should().ContainSingle(link => link.ShareLinkId == created.ShareLinkId && link.Revoked);

        using var auditResponse = await client.GetAsync($"/api/audit?tenantId={tenantId}");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<AuditEventResponse>>();
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "external_share_changed" &&
            auditEvent.ReasonCode == "external_share_link_created" &&
            auditEvent.FileId == fileId &&
            auditEvent.UserId == adminUserId);
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "external_share_changed" &&
            auditEvent.ReasonCode == "external_share_link_revoked" &&
            auditEvent.FileId == fileId &&
            auditEvent.UserId == adminUserId);
    }

    [Fact]
    public async Task Admin_create_external_share_link_validates_request_and_file_state()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var missingFile = await CreateShareLinkAsync(
            client,
            tenantId,
            Guid.NewGuid(),
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(10),
            maxUses: 1);
        missingFile.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var wrongTenant = await CreateShareLinkAsync(
            client,
            Guid.NewGuid(),
            fileId,
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(10),
            maxUses: 1);
        wrongTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var invalidEmail = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "not-email",
            DateTimeOffset.UtcNow.AddMinutes(10),
            maxUses: 1);
        invalidEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidEmail.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("invalid_guest_email"));

        using var expiredLink = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            maxUses: 1);
        expiredLink.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await expiredLink.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("invalid_expires_at"));

        using var invalidMaxUses = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(10),
            maxUses: 0);
        invalidMaxUses.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidMaxUses.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("invalid_max_uses"));

        using var beyondFileExpiry = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddHours(2),
            maxUses: 1);
        beyondFileExpiry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await beyondFileExpiry.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("share_link_exceeds_file_expiry"));

        using var revokeFile = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/revoke", new
        {
            tenantId,
            adminUserId
        });
        revokeFile.StatusCode.Should().Be(HttpStatusCode.OK);

        using var revokedFile = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(10),
            maxUses: 1);
        revokedFile.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await revokedFile.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("file_revoked"));
    }

    [Fact]
    public async Task Admin_external_share_link_revoke_is_tenant_and_file_scoped()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var otherFileId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(client, tenantId, fileId, Guid.NewGuid());
        using var otherRegister = await RegisterFileAsync(client, tenantId, otherFileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        otherRegister.StatusCode.Should().Be(HttpStatusCode.Created);

        using var create = await CreateShareLinkAsync(
            client,
            tenantId,
            fileId,
            adminUserId,
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(10),
            maxUses: 1);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        using var wrongTenant = await RevokeShareLinkAsync(
            client,
            otherTenantId,
            fileId,
            created!.ShareLinkId,
            adminUserId);
        wrongTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var wrongFile = await RevokeShareLinkAsync(
            client,
            tenantId,
            otherFileId,
            created.ShareLinkId,
            adminUserId);
        wrongFile.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var links = await client.GetFromJsonAsync<List<ExternalShareLinkResponse>>(
            $"/api/admin/files/{fileId}/share-links?tenantId={tenantId}");
        links.Should().ContainSingle(link => link.ShareLinkId == created.ShareLinkId && !link.Revoked);
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
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

    private static Task<HttpResponseMessage> RegisterFileAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType = "application/pdf",
        string permissions = "View")
    {
        return client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType,
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions,
            watermarkTemplate = "user:{userId}"
        });
    }

    private static Task<HttpResponseMessage> CreateGroupAsync(HttpClient client, Guid tenantId, Guid groupId)
    {
        return client.PostAsJsonAsync("/api/admin/groups", new
        {
            tenantId,
            groupId,
            name = "Legal"
        });
    }

    private static Task<HttpResponseMessage> AddMemberAsync(HttpClient client, Guid tenantId, Guid groupId, Guid userId)
    {
        return client.PostAsJsonAsync($"/api/admin/groups/{groupId}/members", new
        {
            tenantId,
            userId
        });
    }

    private static Task<HttpResponseMessage> UpsertGrantAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        string subjectType,
        Guid subjectId,
        string permissions)
    {
        return client.PostAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            subjectType,
            subjectId,
            permissions
        });
    }

    private static Task<HttpResponseMessage> ReplaceGrantsAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        IReadOnlyList<GrantRequest> grants)
    {
        return client.PutAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            grants
        });
    }

    private static Task<HttpResponseMessage> CreatePolicyTemplateAsync(
        HttpClient client,
        Guid tenantId,
        Guid templateId,
        string name,
        string permissions,
        string watermarkTemplate,
        int offlineLeaseMinutes = 15)
    {
        return client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name,
            permissions,
            watermarkTemplate,
            offlineLeaseMinutes,
            allowPrint = true
        });
    }

    private static Task<HttpResponseMessage> ApplyPolicyTemplateAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid templateId,
        Guid adminUserId)
    {
        return client.PostAsJsonAsync($"/api/admin/files/{fileId}/apply-policy-template", new
        {
            tenantId,
            templateId,
            adminUserId
        });
    }

    private static Task<HttpResponseMessage> CreateShareLinkAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid adminUserId,
        string guestEmail,
        DateTimeOffset expiresAtUtc,
        int maxUses)
    {
        return client.PostAsJsonAsync($"/api/admin/files/{fileId}/share-links", new
        {
            tenantId,
            adminUserId,
            guestEmail,
            expiresAtUtc,
            maxUses
        });
    }

    private static Task<HttpResponseMessage> RevokeShareLinkAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid shareLinkId,
        Guid adminUserId)
    {
        return client.PostAsJsonAsync($"/api/admin/files/{fileId}/share-links/{shareLinkId}/revoke", new
        {
            tenantId,
            adminUserId
        });
    }

    private sealed record ErrorResponse(string ReasonCode);

    private sealed record GrantRequest(string SubjectType, Guid SubjectId, string Permissions);

    private sealed record FileGrantResponse(
        Guid TenantId,
        Guid FileId,
        string SubjectType,
        Guid SubjectId,
        string Permissions);

    private sealed record FileResponse(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string WatermarkTemplate,
        bool Revoked);

    private sealed record RevokeFileResponse(Guid TenantId, Guid FileId, bool Revoked);

    private sealed record CreateExternalShareLinkResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        bool Revoked,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? RevokedAtUtc,
        string AccessToken);

    private sealed record ExternalShareLinkResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        bool Revoked,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? RevokedAtUtc);

    private sealed record AuditEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc = null);
}
