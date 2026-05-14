using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminGroupsEndpoints
{
    public static IEndpointRouteBuilder MapAdminGroupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/groups");

        group.MapPost("/", CreateGroupAsync);
        group.MapPost("/{groupId:guid}/members", AddMemberAsync);
        group.MapGet("/{groupId:guid}/members", ListMembersAsync);

        return endpoints;
    }

    private static async Task<Results<Created<GroupResponse>, Conflict>> CreateGroupAsync(
        CreateGroupRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await GroupExistsAsync(dbContext, request.TenantId, request.GroupId, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        var group = new TenantGroupEntity
        {
            TenantId = request.TenantId,
            GroupId = request.GroupId,
            Name = request.Name,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantGroups.Add(group);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "group_created"));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await GroupExistsAsync(dbContext, request.TenantId, request.GroupId, cancellationToken))
            {
                return TypedResults.Conflict();
            }

            throw;
        }

        return TypedResults.Created($"/api/admin/groups/{group.GroupId}", GroupResponse.From(group));
    }

    private static async Task<Results<Created<GroupMemberResponse>, Conflict, NotFound>> AddMemberAsync(
        Guid groupId,
        AddMemberRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await GroupExistsAsync(dbContext, request.TenantId, groupId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (await GroupMemberExistsAsync(dbContext, request.TenantId, groupId, request.UserId, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        var member = new GroupMemberEntity
        {
            TenantId = request.TenantId,
            GroupId = groupId,
            UserId = request.UserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.GroupMembers.Add(member);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, request.UserId, "group_member_added"));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await GroupMemberExistsAsync(dbContext, request.TenantId, groupId, request.UserId, cancellationToken))
            {
                return TypedResults.Conflict();
            }

            throw;
        }

        return TypedResults.Created($"/api/admin/groups/{member.GroupId}/members/{member.UserId}", GroupMemberResponse.From(member));
    }

    private static async Task<IReadOnlyList<GroupMemberResponse>> ListMembersAsync(
        Guid groupId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.GroupMembers
            .AsNoTracking()
            .Where(member => member.TenantId == tenantId && member.GroupId == groupId)
            .OrderBy(member => member.UserId)
            .Select(member => GroupMemberResponse.From(member))
            .ToListAsync(cancellationToken);
    }

    private static Task<bool> GroupExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        return dbContext.TenantGroups
            .AsNoTracking()
            .AnyAsync(group => group.TenantId == tenantId && group.GroupId == groupId, cancellationToken);
    }

    private static Task<bool> GroupMemberExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return dbContext.GroupMembers
            .AsNoTracking()
            .AnyAsync(member =>
                member.TenantId == tenantId &&
                member.GroupId == groupId &&
                member.UserId == userId,
                cancellationToken);
    }

    private sealed record CreateGroupRequest(Guid TenantId, Guid GroupId, string Name);

    private sealed record AddMemberRequest(Guid TenantId, Guid UserId);

    private sealed record GroupResponse(Guid TenantId, Guid GroupId, string Name)
    {
        public static GroupResponse From(TenantGroupEntity group)
            => new(group.TenantId, group.GroupId, group.Name);
    }

    private sealed record GroupMemberResponse(Guid TenantId, Guid GroupId, Guid UserId)
    {
        public static GroupMemberResponse From(GroupMemberEntity member)
            => new(member.TenantId, member.GroupId, member.UserId);
    }
}
