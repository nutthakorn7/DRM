using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminFileTagsEndpoints
{
    private const int MaxTagLength = 64;

    public static IEndpointRouteBuilder MapAdminFileTagsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/files/{fileId:guid}/tags");

        group.MapPost("/", AddTagAsync);
        group.MapDelete("/{tag}", RemoveTagAsync);
        group.MapGet("/", ListTagsAsync);

        endpoints.MapGet("/api/admin/tags", ListAllTagsAsync);
        endpoints.MapGet("/api/admin/files-by-tag", ListFilesByTagAsync);

        return endpoints;
    }

    private static async Task<IResult> AddTagAsync(
        Guid fileId,
        AddTagRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        var tag = request.Tag?.Trim();
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > MaxTagLength)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tag"));
        }

        var exists = await dbContext.FileTags
            .AsNoTracking()
            .AnyAsync(t => t.TenantId == request.TenantId && t.FileId == fileId && t.Tag == tag, cancellationToken);

        if (exists)
        {
            return TypedResults.Conflict();
        }

        dbContext.FileTags.Add(new FileTagEntity
        {
            TenantId = request.TenantId,
            FileId = fileId,
            Tag = tag,
            AssignedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict();
        }

        return TypedResults.Created($"/api/admin/files/{fileId}/tags");
    }

    private static async Task<IResult> RemoveTagAsync(
        Guid fileId,
        string tag,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, tenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (tenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        var entity = await dbContext.FileTags
            .SingleOrDefaultAsync(
                t => t.TenantId == tenantId && t.FileId == fileId && t.Tag == tag,
                cancellationToken);

        if (entity is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.FileTags.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListTagsAsync(
        Guid fileId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRead, tenantId, out var fail))
            return fail!;
        var tags = await dbContext.FileTags
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.FileId == fileId)
            .OrderBy(t => t.Tag)
            .Select(t => t.Tag)
            .ToListAsync(cancellationToken);
        return Results.Ok(tags);
    }

    private static async Task<IResult> ListAllTagsAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRead, tenantId, out var fail))
            return fail!;
        var rows = await dbContext.FileTags
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.Tag)
            .ToListAsync(cancellationToken);

        var result = rows
            .GroupBy(tag => tag, StringComparer.Ordinal)
            .Select(g => new TagSummary(g.Key, g.Count()))
            .OrderBy(s => s.Tag, StringComparer.Ordinal)
            .ToList();
        return Results.Ok(result);
    }

    private static async Task<IResult> ListFilesByTagAsync(
        Guid tenantId,
        string tag,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRead, tenantId, out var fail))
            return fail!;
        var fileIds = await dbContext.FileTags
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Tag == tag)
            .OrderBy(t => t.FileId)
            .Select(t => t.FileId)
            .ToListAsync(cancellationToken);
        return Results.Ok(fileIds);
    }

    private sealed record AddTagRequest(Guid TenantId, string Tag);

    private sealed record TagSummary(string Tag, int FileCount);

    private sealed record ErrorResponse(string ReasonCode);
}
