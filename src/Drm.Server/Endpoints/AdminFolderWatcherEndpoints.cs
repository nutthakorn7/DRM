using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminFolderWatcherEndpoints
{
    public static IEndpointRouteBuilder MapAdminFolderWatcherEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/folder-watcher");

        group.MapPut("/config", UpsertConfigAsync);
        group.MapGet("/config", GetConfigAsync);
        group.MapPost("/report", ReportAsync);
        group.MapPost("/events", PostEventAsync);
        group.MapGet("/events", ListEventsAsync);

        return endpoints;
    }

    private static async Task<IResult> UpsertConfigAsync(
        UpsertConfigRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        var foldersJson = request.WatchedFolders is null
            ? "[]"
            : JsonSerializer.Serialize(request.WatchedFolders);
        var existing = await dbContext.TenantFolderWatcherConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);
        var isNew = existing is null;
        if (existing is null)
        {
            existing = new TenantFolderWatcherConfigEntity { TenantId = request.TenantId };
            dbContext.TenantFolderWatcherConfigs.Add(existing);
        }
        existing.WatchedFoldersJson = foldersJson;
        existing.Enabled = request.Enabled;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildResponseAsync(dbContext, request.TenantId, cancellationToken);
        return isNew
            ? TypedResults.Created("/api/admin/folder-watcher/config", response)
            : TypedResults.Ok(response);
    }

    private static async Task<IResult> GetConfigAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsRead, out var fail))
            return fail!;
        var exists = await dbContext.TenantFolderWatcherConfigs
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId, cancellationToken);
        if (!exists)
        {
            return Results.NotFound();
        }
        return Results.Ok(await BuildResponseAsync(dbContext, tenantId, cancellationToken));
    }

    private static async Task<IResult> ReportAsync(
        ServiceReportRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        var config = await dbContext.TenantFolderWatcherConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);
        if (config is null)
        {
            return Results.NotFound();
        }
        config.LastReportStatus = request.Status;
        config.LastReportAtUtc = DateTimeOffset.UtcNow;
        config.LastFilesProtected = request.FilesProtected;
        config.Hostname = request.Hostname;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PostEventAsync(
        ServiceEventRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }
        dbContext.FolderWatcherEvents.Add(new FolderWatcherEventEntity
        {
            TenantId = request.TenantId,
            Hostname = request.Hostname ?? string.Empty,
            FolderPath = request.FolderPath ?? string.Empty,
            FileName = request.FileName ?? string.Empty,
            FileSize = request.FileSize,
            Status = request.Status ?? string.Empty,
            FileId = request.FileId,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Created("/api/admin/folder-watcher/events");
    }

    private static async Task<IResult> ListEventsAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsRead, out var fail))
            return fail!;
        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        var events = await dbContext.FolderWatcherEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.Id)
            .Take(pageSize)
            .Select(e => new FolderWatcherEventResponse(
                e.Id, e.Hostname, e.FolderPath, e.FileName, e.FileSize, e.Status, e.FileId, e.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        return Results.Ok(events);
    }

    private static async Task<FolderWatcherConfigResponse> BuildResponseAsync(
        AppDbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
    {
        var config = await dbContext.TenantFolderWatcherConfigs
            .AsNoTracking()
            .SingleAsync(c => c.TenantId == tenantId, cancellationToken);
        IReadOnlyList<WatchedFolder> folders = Array.Empty<WatchedFolder>();
        try
        {
            folders = JsonSerializer.Deserialize<List<WatchedFolder>>(config.WatchedFoldersJson)
                ?? new List<WatchedFolder>();
        }
        catch (JsonException) { }
        return new FolderWatcherConfigResponse(
            config.TenantId,
            folders,
            config.Enabled,
            config.LastReportStatus,
            config.LastReportAtUtc,
            config.LastFilesProtected,
            config.Hostname,
            config.UpdatedAtUtc);
    }

    public sealed record WatchedFolder(string Path, Guid? PolicyTemplateId);

    private sealed record UpsertConfigRequest(
        Guid TenantId,
        IReadOnlyList<WatchedFolder>? WatchedFolders,
        bool Enabled);

    private sealed record FolderWatcherConfigResponse(
        Guid TenantId,
        IReadOnlyList<WatchedFolder> WatchedFolders,
        bool Enabled,
        string? LastReportStatus,
        DateTimeOffset? LastReportAtUtc,
        int LastFilesProtected,
        string? Hostname,
        DateTimeOffset UpdatedAtUtc);

    private sealed record ServiceReportRequest(
        Guid TenantId,
        string Hostname,
        string Status,
        int FilesProtected);

    private sealed record ServiceEventRequest(
        Guid TenantId,
        string? Hostname,
        string? FolderPath,
        string? FileName,
        long FileSize,
        string? Status,
        Guid? FileId);

    private sealed record FolderWatcherEventResponse(
        long Id,
        string Hostname,
        string FolderPath,
        string FileName,
        long FileSize,
        string Status,
        Guid? FileId,
        DateTimeOffset OccurredAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
