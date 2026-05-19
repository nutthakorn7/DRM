using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminSecureContainersEndpoints
{
    public static IEndpointRouteBuilder MapAdminSecureContainersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/secure-containers");

        group.MapPost("/", RegisterAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{containerId:guid}", GetAsync);
        group.MapDelete("/{containerId:guid}", DeleteAsync);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty || request.ContainerId == Guid.Empty || request.OwnerUserId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_display_name"));
        }
        if (request.Files is null || request.Files.Count == 0)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_files"));
        }

        var exists = await dbContext.SecureContainers
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == request.TenantId && c.ContainerId == request.ContainerId, cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict();
        }

        var totalBytes = request.Files.Sum(f => f.Size);
        dbContext.SecureContainers.Add(new SecureContainerEntity
        {
            TenantId = request.TenantId,
            ContainerId = request.ContainerId,
            OwnerUserId = request.OwnerUserId,
            DisplayName = request.DisplayName.Trim(),
            FileCount = request.Files.Count,
            TotalBytes = totalBytes,
            PolicyTemplateId = request.PolicyTemplateId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        var ordinal = 0;
        foreach (var file in request.Files)
        {
            dbContext.SecureContainerFiles.Add(new SecureContainerFileEntity
            {
                TenantId = request.TenantId,
                ContainerId = request.ContainerId,
                OrdinalIndex = ordinal++,
                RelativePath = file.RelativePath,
                Size = file.Size
            });
        }
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = request.ContainerId,
            ActorAdminId = AdminAudit.ActorId(httpContext),
            EventType = "system_changed",
            ReasonCode = "secure_container_registered",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/admin/secure-containers/{request.ContainerId}?tenantId={request.TenantId}",
            await BuildResponseAsync(dbContext, request.TenantId, request.ContainerId, cancellationToken));
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsRead, tenantId, out var fail))
            return fail!;
        var items = await dbContext.SecureContainers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.DisplayName)
            .Select(c => new ContainerSummary(
                c.TenantId, c.ContainerId, c.OwnerUserId, c.DisplayName,
                c.FileCount, c.TotalBytes, c.PolicyTemplateId, c.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(
        Guid containerId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsRead, tenantId, out var fail))
            return fail!;
        var container = await dbContext.SecureContainers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.ContainerId == containerId, cancellationToken);
        if (container is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(await BuildResponseAsync(dbContext, tenantId, containerId, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(
        Guid containerId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsWrite, tenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        var container = await dbContext.SecureContainers
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.ContainerId == containerId, cancellationToken);
        if (container is null)
        {
            return Results.NotFound();
        }
        var files = dbContext.SecureContainerFiles
            .Where(f => f.TenantId == tenantId && f.ContainerId == containerId);
        dbContext.SecureContainerFiles.RemoveRange(files);
        dbContext.SecureContainers.Remove(container);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<ContainerResponse> BuildResponseAsync(
        AppDbContext dbContext, Guid tenantId, Guid containerId, CancellationToken cancellationToken)
    {
        var container = await dbContext.SecureContainers
            .AsNoTracking()
            .SingleAsync(c => c.TenantId == tenantId && c.ContainerId == containerId, cancellationToken);
        var files = await dbContext.SecureContainerFiles
            .AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.ContainerId == containerId)
            .OrderBy(f => f.OrdinalIndex)
            .Select(f => new ContainerFileResponse(f.OrdinalIndex, f.RelativePath, f.Size))
            .ToListAsync(cancellationToken);
        return new ContainerResponse(
            container.TenantId,
            container.ContainerId,
            container.OwnerUserId,
            container.DisplayName,
            container.FileCount,
            container.TotalBytes,
            container.PolicyTemplateId,
            container.CreatedAtUtc,
            files);
    }

    private sealed record RegisterRequest(
        Guid TenantId,
        Guid ContainerId,
        Guid OwnerUserId,
        string DisplayName,
        Guid? PolicyTemplateId,
        IReadOnlyList<RegisterFile> Files);

    private sealed record RegisterFile(string RelativePath, long Size);

    private sealed record ContainerSummary(
        Guid TenantId,
        Guid ContainerId,
        Guid OwnerUserId,
        string DisplayName,
        int FileCount,
        long TotalBytes,
        Guid? PolicyTemplateId,
        DateTimeOffset CreatedAtUtc);

    private sealed record ContainerResponse(
        Guid TenantId,
        Guid ContainerId,
        Guid OwnerUserId,
        string DisplayName,
        int FileCount,
        long TotalBytes,
        Guid? PolicyTemplateId,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<ContainerFileResponse> Files);

    private sealed record ContainerFileResponse(int OrdinalIndex, string RelativePath, long Size);

    private sealed record ErrorResponse(string ReasonCode);
}
