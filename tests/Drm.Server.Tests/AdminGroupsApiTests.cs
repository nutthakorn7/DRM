using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminGroupsApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-groups-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminGroupsApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_create_group_and_add_member()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var createGroup = await client.PostAsJsonAsync("/api/admin/groups", new
        {
            tenantId,
            groupId,
            name = "Legal"
        });

        createGroup.StatusCode.Should().Be(HttpStatusCode.Created);

        using var addMember = await AddMemberAsync(client, tenantId, groupId, userId);

        addMember.StatusCode.Should().Be(HttpStatusCode.Created);

        var members = await client.GetFromJsonAsync<List<GroupMemberResponse>>(
            $"/api/admin/groups/{groupId}/members?tenantId={tenantId}");

        members.Should().NotBeNull();
        members.Should().ContainSingle(member => member.TenantId == tenantId && member.GroupId == groupId && member.UserId == userId);

        using var auditResponse = await client.GetAsync($"/api/audit?tenantId={tenantId}");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<AuditEventResponse>>();

        auditEvents.Should().NotBeNull();
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "system_changed" &&
            auditEvent.ReasonCode == "group_created");
        auditEvents.Should().Contain(auditEvent =>
            auditEvent.EventType == "system_changed" &&
            auditEvent.ReasonCode == "group_member_added" &&
            auditEvent.UserId == userId);
    }

    [Fact]
    public async Task Admin_create_group_returns_conflict_for_duplicate_group_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        using var firstCreate = await CreateGroupAsync(client, tenantId, groupId, "Legal");
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicateCreate = await CreateGroupAsync(client, tenantId, groupId, "Finance");

        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_add_member_returns_conflict_for_duplicate_member_in_same_tenant_group()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var createGroup = await CreateGroupAsync(client, tenantId, groupId, "Legal");
        createGroup.StatusCode.Should().Be(HttpStatusCode.Created);

        using var firstAdd = await AddMemberAsync(client, tenantId, groupId, userId);
        firstAdd.StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicateAdd = await AddMemberAsync(client, tenantId, groupId, userId);

        duplicateAdd.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_add_member_returns_not_found_for_missing_group_in_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var addMember = await AddMemberAsync(client, tenantId, Guid.NewGuid(), Guid.NewGuid());

        addMember.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_list_members_is_scoped_to_tenant()
    {
        using var client = factory.CreateClient();
        var groupId = Guid.NewGuid();
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var highFirstTenantUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var lowFirstTenantUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondUserId = Guid.NewGuid();

        using var firstGroup = await CreateGroupAsync(client, firstTenantId, groupId, "Legal");
        using var secondGroup = await CreateGroupAsync(client, secondTenantId, groupId, "Legal");
        firstGroup.StatusCode.Should().Be(HttpStatusCode.Created);
        secondGroup.StatusCode.Should().Be(HttpStatusCode.Created);

        using var firstAdd = await AddMemberAsync(client, firstTenantId, groupId, highFirstTenantUserId);
        using var secondFirstTenantAdd = await AddMemberAsync(client, firstTenantId, groupId, lowFirstTenantUserId);
        using var secondAdd = await AddMemberAsync(client, secondTenantId, groupId, secondUserId);
        firstAdd.StatusCode.Should().Be(HttpStatusCode.Created);
        secondFirstTenantAdd.StatusCode.Should().Be(HttpStatusCode.Created);
        secondAdd.StatusCode.Should().Be(HttpStatusCode.Created);

        var members = await client.GetFromJsonAsync<List<GroupMemberResponse>>(
            $"/api/admin/groups/{groupId}/members?tenantId={firstTenantId}");

        members.Should().NotBeNull();
        members!.Select(member => member.UserId).Should().Equal(lowFirstTenantUserId, highFirstTenantUserId);
        members.Should().NotContain(member => member.UserId == secondUserId);
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

    private static Task<HttpResponseMessage> CreateGroupAsync(
        HttpClient client,
        Guid tenantId,
        Guid groupId,
        string name)
    {
        return client.PostAsJsonAsync("/api/admin/groups", new
        {
            tenantId,
            groupId,
            name
        });
    }

    private static Task<HttpResponseMessage> AddMemberAsync(
        HttpClient client,
        Guid tenantId,
        Guid groupId,
        Guid userId)
    {
        return client.PostAsJsonAsync($"/api/admin/groups/{groupId}/members", new
        {
            tenantId,
            userId
        });
    }

    private sealed record GroupMemberResponse(Guid TenantId, Guid GroupId, Guid UserId);

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
    public async Task Create_group_with_mismatched_header_returns_400()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/groups")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                groupId = Guid.NewGuid(),
                name = "Finance",
            })
        };
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record ErrorBody(string ReasonCode);
}
