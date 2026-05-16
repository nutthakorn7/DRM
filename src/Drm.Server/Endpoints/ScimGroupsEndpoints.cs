using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Drm.Server.Endpoints;

public static class ScimGroupsEndpoints
{
    public static IEndpointRouteBuilder MapScimGroupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/scim/v2/{tenantId:guid}/Groups");

        group.MapGet("/", ListGroupsAsync);
        group.MapPost("/", CreateGroupAsync);
        group.MapGet("/{id}", GetGroupAsync);
        group.MapPut("/{id}", ReplaceGroupAsync);
        group.MapDelete("/{id}", DeleteGroupAsync);

        return endpoints;
    }

    private static async Task<IResult> ListGroupsAsync(
        Guid tenantId,
        string? filter,
        int? startIndex,
        int? count,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var start = Math.Max(1, startIndex ?? 1);
        var pageSize = Math.Min(200, Math.Max(1, count ?? 100));

        var query = dbContext.TenantGroups
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var (displayName, externalId) = ParseFilter(filter);
            if (displayName != null)
                query = query.Where(g => g.Name == displayName);
            else if (externalId != null)
                query = query.Where(g => g.ExternalId == externalId);
        }

        var total = await query.CountAsync(cancellationToken);
        var groups = await query
            .OrderBy(g => g.Name)
            .Skip(start - 1)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var groupIds = groups.Select(g => g.GroupId).ToList();
        var allMembers = await dbContext.GroupMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && groupIds.Contains(m.GroupId))
            .Join(dbContext.TenantUsers.AsNoTracking(),
                m => new { m.TenantId, m.UserId },
                u => new { u.TenantId, u.UserId },
                (m, u) => new { m.GroupId, UserId = u.UserId.ToString(), u.DisplayName })
            .ToListAsync(cancellationToken);

        var membersByGroup = allMembers
            .GroupBy(x => x.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ScimGroupMember>)g
                .Select(x => new ScimGroupMember(x.UserId, x.DisplayName))
                .ToList());

        var response = new ScimListResponse<ScimGroup>
        {
            TotalResults = total,
            StartIndex = start,
            ItemsPerPage = groups.Count,
            Resources = groups.Select(g =>
                ToScimGroup(g, tenantId, membersByGroup.TryGetValue(g.GroupId, out var m) ? m : [])).ToList()
        };

        return Results.Json(response, contentType: "application/scim+json");
    }

    private static async Task<IResult> CreateGroupAsync(
        Guid tenantId,
        ScimGroupRequest request,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.Json(new ScimError { Status = "400", Detail = "displayName is required." },
                contentType: "application/scim+json", statusCode: 400);

        var groupId = Guid.NewGuid();
        var group = new TenantGroupEntity
        {
            TenantId = tenantId,
            GroupId = groupId,
            Name = request.DisplayName,
            ExternalId = request.ExternalId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantGroups.Add(group);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(tenantId, null, "group_created"));

        var members = await AddMembersAsync(dbContext, tenantId, groupId, request.Members, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var scimGroup = ToScimGroup(group, tenantId, members);
        httpContext.Response.Headers.Location = scimGroup.Meta.Location;
        return Results.Json(scimGroup, contentType: "application/scim+json", statusCode: 201);
    }

    private static async Task<IResult> GetGroupAsync(
        Guid tenantId,
        string id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var groupId))
            return Results.Json(new ScimError { Status = "404", Detail = "Group not found." },
                contentType: "application/scim+json", statusCode: 404);

        var group = await dbContext.TenantGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.GroupId == groupId, cancellationToken);

        if (group == null)
            return Results.Json(new ScimError { Status = "404", Detail = "Group not found." },
                contentType: "application/scim+json", statusCode: 404);

        var members = await GetMembersAsync(dbContext, tenantId, groupId, cancellationToken);
        return Results.Json(ToScimGroup(group, tenantId, members), contentType: "application/scim+json");
    }

    private static async Task<IResult> ReplaceGroupAsync(
        Guid tenantId,
        string id,
        ScimGroupRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var groupId))
            return Results.Json(new ScimError { Status = "404", Detail = "Group not found." },
                contentType: "application/scim+json", statusCode: 404);

        var group = await dbContext.TenantGroups
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.GroupId == groupId, cancellationToken);

        if (group == null)
            return Results.Json(new ScimError { Status = "404", Detail = "Group not found." },
                contentType: "application/scim+json", statusCode: 404);

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            group.Name = request.DisplayName;
        if (request.ExternalId != null)
            group.ExternalId = request.ExternalId;

        // Replace entire member list
        dbContext.GroupMembers.RemoveRange(
            dbContext.GroupMembers.Where(m => m.TenantId == tenantId && m.GroupId == groupId));

        var members = await AddMembersAsync(dbContext, tenantId, groupId, request.Members, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(ToScimGroup(group, tenantId, members), contentType: "application/scim+json");
    }

    private static async Task<IResult> DeleteGroupAsync(
        Guid tenantId,
        string id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var groupId))
            return Results.Json(new ScimError { Status = "404", Detail = "Group not found." },
                contentType: "application/scim+json", statusCode: 404);

        var group = await dbContext.TenantGroups
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.GroupId == groupId, cancellationToken);

        if (group == null)
            return Results.Json(new ScimError { Status = "404", Detail = "Group not found." },
                contentType: "application/scim+json", statusCode: 404);

        dbContext.GroupMembers.RemoveRange(
            dbContext.GroupMembers.Where(m => m.TenantId == tenantId && m.GroupId == groupId));
        dbContext.TenantGroups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<List<ScimGroupMember>> AddMembersAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid groupId,
        IReadOnlyList<ScimGroupMemberRef>? memberRefs,
        CancellationToken cancellationToken)
    {
        var result = new List<ScimGroupMember>();
        if (memberRefs == null) return result;

        foreach (var ref_ in memberRefs)
        {
            if (!Guid.TryParse(ref_.Value, out var userId)) continue;
            var user = await dbContext.TenantUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, cancellationToken);
            if (user == null) continue;

            dbContext.GroupMembers.Add(new GroupMemberEntity
            {
                TenantId = tenantId,
                GroupId = groupId,
                UserId = userId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            result.Add(new ScimGroupMember(userId.ToString(), user.DisplayName));
        }

        return result;
    }

    private static async Task<List<ScimGroupMember>> GetMembersAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        return await dbContext.GroupMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.GroupId == groupId)
            .Join(dbContext.TenantUsers.AsNoTracking(),
                m => new { m.TenantId, m.UserId },
                u => new { u.TenantId, u.UserId },
                (m, u) => new ScimGroupMember(u.UserId.ToString(), u.DisplayName))
            .ToListAsync(cancellationToken);
    }

    private static ScimGroup ToScimGroup(TenantGroupEntity group, Guid tenantId, IReadOnlyList<ScimGroupMember> members)
        => new()
        {
            Id = group.GroupId.ToString(),
            ExternalId = group.ExternalId,
            DisplayName = group.Name,
            Members = members,
            Meta = new ScimMeta
            {
                ResourceType = "Group",
                Created = group.CreatedAtUtc,
                LastModified = group.CreatedAtUtc,
                Location = $"/scim/v2/{tenantId}/Groups/{group.GroupId}"
            }
        };

    private static (string? DisplayName, string? ExternalId) ParseFilter(string filter)
    {
        var m = Regex.Match(filter, @"(\w+)\s+eq\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, null);
        return m.Groups[1].Value.ToLowerInvariant() switch
        {
            "displayname" => (m.Groups[2].Value, null),
            "externalid" => (null, m.Groups[2].Value),
            _ => (null, null)
        };
    }
}
