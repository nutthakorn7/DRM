using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Drm.Server.Endpoints;

public static class ScimUsersEndpoints
{
    public static IEndpointRouteBuilder MapScimUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/scim/v2/{tenantId:guid}/Users");

        group.MapGet("/", ListUsersAsync);
        group.MapPost("/", CreateUserAsync);
        group.MapGet("/{id}", GetUserAsync);
        group.MapPut("/{id}", ReplaceUserAsync);
        group.MapDelete("/{id}", DeleteUserAsync);

        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(
        Guid tenantId,
        string? filter,
        int? startIndex,
        int? count,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var start = Math.Max(1, startIndex ?? 1);
        var pageSize = Math.Min(200, Math.Max(1, count ?? 100));

        var query = dbContext.TenantUsers
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var (userName, externalId) = ParseFilter(filter);
            if (userName != null)
                query = query.Where(u => u.Email == userName);
            else if (externalId != null)
                query = query.Where(u => u.ExternalId == externalId);
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(u => u.Email)
            .Skip(start - 1)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = new ScimListResponse<ScimUser>
        {
            TotalResults = total,
            StartIndex = start,
            ItemsPerPage = users.Count,
            Resources = users.Select(u => ToScimUser(u, tenantId)).ToList()
        };

        return Results.Json(response, contentType: "application/scim+json");
    }

    private static async Task<IResult> CreateUserAsync(
        Guid tenantId,
        ScimUserRequest request,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var email = ResolveEmail(request);
        if (string.IsNullOrWhiteSpace(email))
            return Results.Json(new ScimError { Status = "400", Detail = "userName is required." },
                contentType: "application/scim+json", statusCode: 400);

        var conflict = await dbContext.TenantUsers
            .AsNoTracking()
            .AnyAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken);

        if (conflict)
            return Results.Json(new ScimError { Status = "409", Detail = "User with this userName already exists." },
                contentType: "application/scim+json", statusCode: 409);

        var userId = Guid.NewGuid();
        var user = new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = email,
            DisplayName = request.DisplayName ?? email,
            ExternalId = request.ExternalId,
            Active = request.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantUsers.Add(user);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(tenantId, userId, "user_created"));
        await dbContext.SaveChangesAsync(cancellationToken);

        var scimUser = ToScimUser(user, tenantId);
        httpContext.Response.Headers.Location = scimUser.Meta.Location;
        return Results.Json(scimUser, contentType: "application/scim+json", statusCode: 201);
    }

    private static async Task<IResult> GetUserAsync(
        Guid tenantId,
        string id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.Json(new ScimError { Status = "404", Detail = "User not found." },
                contentType: "application/scim+json", statusCode: 404);

        var user = await dbContext.TenantUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, cancellationToken);

        return user == null
            ? Results.Json(new ScimError { Status = "404", Detail = "User not found." },
                contentType: "application/scim+json", statusCode: 404)
            : Results.Json(ToScimUser(user, tenantId), contentType: "application/scim+json");
    }

    private static async Task<IResult> ReplaceUserAsync(
        Guid tenantId,
        string id,
        ScimUserRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.Json(new ScimError { Status = "404", Detail = "User not found." },
                contentType: "application/scim+json", statusCode: 404);

        var user = await dbContext.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, cancellationToken);

        if (user == null)
            return Results.Json(new ScimError { Status = "404", Detail = "User not found." },
                contentType: "application/scim+json", statusCode: 404);

        var email = ResolveEmail(request);
        if (!string.IsNullOrWhiteSpace(email))
            user.Email = email;
        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName;
        if (request.ExternalId != null)
            user.ExternalId = request.ExternalId;
        user.Active = request.Active;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Json(ToScimUser(user, tenantId), contentType: "application/scim+json");
    }

    private static async Task<IResult> DeleteUserAsync(
        Guid tenantId,
        string id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.Json(new ScimError { Status = "404", Detail = "User not found." },
                contentType: "application/scim+json", statusCode: 404);

        var user = await dbContext.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, cancellationToken);

        if (user == null)
            return Results.Json(new ScimError { Status = "404", Detail = "User not found." },
                contentType: "application/scim+json", statusCode: 404);

        dbContext.GroupMembers.RemoveRange(
            dbContext.GroupMembers.Where(m => m.TenantId == tenantId && m.UserId == userId));
        dbContext.TenantUsers.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static ScimUser ToScimUser(TenantUserEntity user, Guid tenantId)
        => new()
        {
            Id = user.UserId.ToString(),
            ExternalId = user.ExternalId,
            UserName = user.Email,
            DisplayName = user.DisplayName,
            Emails = [new ScimEmail(user.Email, true)],
            Active = user.Active,
            Meta = new ScimMeta
            {
                ResourceType = "User",
                Created = user.CreatedAtUtc,
                LastModified = user.CreatedAtUtc,
                Location = $"/scim/v2/{tenantId}/Users/{user.UserId}"
            }
        };

    private static string ResolveEmail(ScimUserRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.UserName)) return request.UserName;
        return request.Emails?.FirstOrDefault(e => e.Primary)?.Value
            ?? request.Emails?.FirstOrDefault()?.Value
            ?? string.Empty;
    }

    private static (string? UserName, string? ExternalId) ParseFilter(string filter)
    {
        var m = Regex.Match(filter, @"(\w+)\s+eq\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, null);
        return m.Groups[1].Value.ToLowerInvariant() switch
        {
            "username" => (m.Groups[2].Value, null),
            "externalid" => (null, m.Groups[2].Value),
            _ => (null, null)
        };
    }
}
