using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
        decision.Should().BeEquivalentTo(new PolicyDecisionResponse(
            true,
            "View",
            "allowed",
            "user:{userId}"));
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

    private sealed record ErrorResponse(string ReasonCode);

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
        string WatermarkTemplate);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate);
}
