using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminFileCollectionEndpoints
{
    public static IEndpointRouteBuilder MapAdminFileCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/collections");
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{collectionId:guid}", GetAsync);
        group.MapPatch("/{collectionId:guid}", UpdateAsync);
        group.MapDelete("/{collectionId:guid}", DeleteAsync);
        group.MapPost("/{collectionId:guid}/files", AddFilesAsync);
        group.MapDelete("/{collectionId:guid}/files/{fileId:guid}", RemoveFileAsync);
        group.MapPost("/{collectionId:guid}/apply-policy", ApplyPolicyAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateCollectionRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == request.TenantId, ct);
        if (tenant is null) return Results.NotFound();

        var collection = new FileCollectionEntity
        {
            CollectionId = Guid.NewGuid(),
            TenantId = request.TenantId,
            Name = request.Name,
            Description = request.Description,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.FileCollections.Add(collection);
        await dbContext.SaveChangesAsync(ct);
        return Results.Created($"/api/admin/collections/{collection.CollectionId}",
            CollectionResponse.From(collection, 0));
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRead, tenantId, out var fail))
            return fail!;

        var collections = await dbContext.FileCollections.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var itemCounts = (await dbContext.FileCollectionItems.AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .ToListAsync(ct))
            .GroupBy(i => i.CollectionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var rows = collections
            .Select(c => CollectionResponse.From(c, itemCounts.GetValueOrDefault(c.CollectionId, 0)))
            .ToList();
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetAsync(
        Guid collectionId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRead, tenantId, out var fail))
            return fail!;

        var collection = await dbContext.FileCollections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CollectionId == collectionId && c.TenantId == tenantId, ct);
        if (collection is null) return Results.NotFound();

        var items = await dbContext.FileCollectionItems.AsNoTracking()
            .Where(i => i.CollectionId == collectionId && i.TenantId == tenantId)
            .ToListAsync(ct);

        return Results.Ok(CollectionDetailResponse.From(collection, items));
    }

    private static async Task<IResult> UpdateAsync(
        Guid collectionId,
        UpdateCollectionRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;

        var collection = await dbContext.FileCollections
            .FirstOrDefaultAsync(c => c.CollectionId == collectionId && c.TenantId == request.TenantId, ct);
        if (collection is null) return Results.NotFound();

        if (request.Name is not null) collection.Name = request.Name;
        if (request.Description is not null) collection.Description = request.Description;
        collection.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var count = await dbContext.FileCollectionItems
            .CountAsync(i => i.CollectionId == collectionId, ct);
        return Results.Ok(CollectionResponse.From(collection, count));
    }

    private static async Task<IResult> DeleteAsync(
        Guid collectionId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, tenantId, out var fail))
            return fail!;

        var collection = await dbContext.FileCollections
            .FirstOrDefaultAsync(c => c.CollectionId == collectionId && c.TenantId == tenantId, ct);
        if (collection is null) return Results.NotFound();

        var items = await dbContext.FileCollectionItems
            .Where(i => i.CollectionId == collectionId)
            .ToListAsync(ct);
        dbContext.FileCollectionItems.RemoveRange(items);
        dbContext.FileCollections.Remove(collection);
        await dbContext.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddFilesAsync(
        Guid collectionId,
        AddFilesToCollectionRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;

        var collection = await dbContext.FileCollections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CollectionId == collectionId && c.TenantId == request.TenantId, ct);
        if (collection is null) return Results.NotFound();

        var existing = (await dbContext.FileCollectionItems.AsNoTracking()
            .Where(i => i.CollectionId == collectionId)
            .ToListAsync(ct))
            .Select(i => i.FileId).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        int added = 0;
        foreach (var fileId in request.FileIds.Distinct())
        {
            if (existing.Contains(fileId)) continue;
            dbContext.FileCollectionItems.Add(new FileCollectionItemEntity
            {
                CollectionId = collectionId,
                FileId = fileId,
                TenantId = request.TenantId,
                AddedAtUtc = now
            });
            added++;
        }
        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(new { Added = added });
    }

    private static async Task<IResult> RemoveFileAsync(
        Guid collectionId,
        Guid fileId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, tenantId, out var fail))
            return fail!;

        var item = await dbContext.FileCollectionItems
            .FirstOrDefaultAsync(i => i.CollectionId == collectionId && i.FileId == fileId && i.TenantId == tenantId, ct);
        if (item is null) return Results.NotFound();

        dbContext.FileCollectionItems.Remove(item);
        await dbContext.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ApplyPolicyAsync(
        Guid collectionId,
        ApplyCollectionPolicyRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.FilesRevoke, request.TenantId, out var fail))
            return fail!;

        var collection = await dbContext.FileCollections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CollectionId == collectionId && c.TenantId == request.TenantId, ct);
        if (collection is null) return Results.NotFound();

        var fileIds = (await dbContext.FileCollectionItems.AsNoTracking()
            .Where(i => i.CollectionId == collectionId && i.TenantId == request.TenantId)
            .ToListAsync(ct))
            .Select(i => i.FileId).ToList();

        if (fileIds.Count == 0) return Results.Ok(new { Updated = 0 });

        var files = await dbContext.ProtectedFiles
            .Where(f => f.TenantId == request.TenantId && fileIds.Contains(f.Id))
            .ToListAsync(ct);

        foreach (var file in files)
        {
            if (request.ExpiresAtUtc.HasValue)
                file.ExpiresAtUtc = request.ExpiresAtUtc.Value;
        }

        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(new { Updated = files.Count });
    }

    private sealed record CreateCollectionRequest(Guid TenantId, string Name, string? Description);
    private sealed record UpdateCollectionRequest(Guid TenantId, string? Name, string? Description);
    private sealed record AddFilesToCollectionRequest(Guid TenantId, List<Guid> FileIds);
    private sealed record ApplyCollectionPolicyRequest(Guid TenantId, DateTimeOffset? ExpiresAtUtc);

    private sealed record CollectionResponse(
        Guid CollectionId, Guid TenantId, string Name, string? Description,
        int FileCount, DateTimeOffset CreatedAtUtc)
    {
        public static CollectionResponse From(FileCollectionEntity c, int count) =>
            new(c.CollectionId, c.TenantId, c.Name, c.Description, count, c.CreatedAtUtc);
    }

    private sealed record CollectionDetailResponse(
        Guid CollectionId, Guid TenantId, string Name, string? Description,
        List<Guid> FileIds, DateTimeOffset CreatedAtUtc)
    {
        public static CollectionDetailResponse From(FileCollectionEntity c, List<FileCollectionItemEntity> items) =>
            new(c.CollectionId, c.TenantId, c.Name, c.Description,
                items.Select(i => i.FileId).ToList(), c.CreatedAtUtc);
    }
}
