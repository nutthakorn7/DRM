using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminDirectorySyncEndpoints
{
    public static IEndpointRouteBuilder MapAdminDirectorySyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/directory");

        group.MapPut("/config", UpsertConfigAsync);
        group.MapGet("/config", GetConfigAsync);
        group.MapPost("/sync", TriggerSyncAsync);

        return endpoints;
    }

    private static async Task<Results<Created<DirectorySyncConfigResponse>, Ok<DirectorySyncConfigResponse>>> UpsertConfigAsync(
        DirectorySyncConfigRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.TenantDirectorySyncConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);

        bool isNew = existing == null;

        if (existing == null)
        {
            existing = new TenantDirectorySyncConfigEntity { TenantId = request.TenantId };
            dbContext.TenantDirectorySyncConfigs.Add(existing);
        }

        existing.EntraTenantId = request.EntraTenantId;
        existing.ClientId = request.ClientId;
        existing.ClientSecret = request.ClientSecret;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = DirectorySyncConfigResponse.From(existing);
        return isNew
            ? TypedResults.Created("/api/admin/directory/config", response)
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<DirectorySyncConfigResponse>, NotFound>> GetConfigAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.TenantDirectorySyncConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        return config == null
            ? TypedResults.NotFound()
            : TypedResults.Ok(DirectorySyncConfigResponse.From(config));
    }

    private static async Task<Results<Ok<SyncResultResponse>, NotFound>> TriggerSyncAsync(
        TriggerSyncRequest request,
        IDirectorySyncService syncService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await syncService.SyncAsync(request.TenantId, cancellationToken);
            return TypedResults.Ok(new SyncResultResponse(
                result.UsersUpserted,
                result.GroupsUpserted,
                result.MembershipsUpserted));
        }
        catch (InvalidOperationException)
        {
            return TypedResults.NotFound();
        }
    }

    private sealed record DirectorySyncConfigRequest(
        Guid TenantId,
        string EntraTenantId,
        string ClientId,
        string ClientSecret);

    private sealed record TriggerSyncRequest(Guid TenantId);

    private sealed record DirectorySyncConfigResponse(
        Guid TenantId,
        string EntraTenantId,
        string ClientId,
        DateTimeOffset? LastSyncAtUtc,
        string? LastSyncStatus,
        int? LastSyncUserCount,
        int? LastSyncGroupCount)
    {
        public static DirectorySyncConfigResponse From(TenantDirectorySyncConfigEntity c)
            => new(c.TenantId, c.EntraTenantId, c.ClientId,
                   c.LastSyncAtUtc, c.LastSyncStatus, c.LastSyncUserCount, c.LastSyncGroupCount);
    }

    private sealed record SyncResultResponse(int UsersUpserted, int GroupsUpserted, int MembershipsUpserted);
}
