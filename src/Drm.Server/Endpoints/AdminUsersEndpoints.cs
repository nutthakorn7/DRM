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

    private static async Task<Results<Created<UserResponse>, Conflict, BadRequest<ErrorResponse>>> CreateUserAsync(
        CreateUserRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return TypedResults.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (await ConflictingUserExistsAsync(dbContext, request.TenantId, request.UserId, request.Email, cancellationToken))
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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await ConflictingUserExistsAsync(dbContext, request.TenantId, request.UserId, request.Email, cancellationToken))
            {
                return TypedResults.Conflict();
            }

            throw;
        }

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

    private static Task<bool> ConflictingUserExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        return dbContext.TenantUsers
            .AsNoTracking()
            .AnyAsync(user =>
                user.TenantId == tenantId &&
                (user.UserId == userId || user.Email == email),
                cancellationToken);
    }

    private sealed record CreateUserRequest(Guid TenantId, Guid UserId, string Email, string DisplayName);

    private sealed record ErrorResponse(string ReasonCode);

    private sealed record UserResponse(Guid UserId, Guid TenantId, string Email, string DisplayName)
    {
        public static UserResponse From(TenantUserEntity user)
            => new(user.UserId, user.TenantId, user.Email, user.DisplayName);
    }
}
