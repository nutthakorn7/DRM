using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminUsersEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/users");

        group.MapPost("/", CreateUserAsync);
        group.MapGet("/", ListUsersAsync);

        return endpoints;
    }

    private static async Task<Results<Created<UserResponse>, Conflict>> CreateUserAsync(
        CreateUserRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.TenantUsers
            .AnyAsync(user => user.TenantId == request.TenantId && user.UserId == request.UserId, cancellationToken);

        if (exists)
        {
            return TypedResults.Conflict();
        }

        var user = new TenantUserEntity
        {
            TenantId = request.TenantId,
            UserId = request.UserId,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantUsers.Add(user);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, request.UserId, "user_created"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/users/{user.UserId}", UserResponse.From(user));
    }

    private static async Task<IReadOnlyList<UserResponse>> ListUsersAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.TenantUsers
            .AsNoTracking()
            .Where(user => user.TenantId == tenantId)
            .OrderBy(user => user.Email)
            .Select(user => UserResponse.From(user))
            .ToListAsync(cancellationToken);
    }

    private sealed record CreateUserRequest(Guid TenantId, Guid UserId, string Email, string DisplayName);

    private sealed record UserResponse(Guid UserId, Guid TenantId, string Email, string DisplayName)
    {
        public static UserResponse From(TenantUserEntity user)
            => new(user.UserId, user.TenantId, user.Email, user.DisplayName);
    }
}
