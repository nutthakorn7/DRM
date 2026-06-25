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

    private static async Task<IResult> UpsertConfigAsync(
        DirectorySyncConfigRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        IDirectorySecretProtector secretProtector,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.IntegrationsWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

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

        existing.Provider = string.IsNullOrWhiteSpace(request.Provider) ? "entra" : request.Provider.Trim().ToLowerInvariant();
        existing.LdapHost = request.LdapHost ?? string.Empty;
        existing.LdapPort = request.LdapPort ?? 636;
        existing.LdapUseLdaps = request.LdapUseLdaps ?? true;
        existing.LdapBindDn = request.LdapBindDn ?? string.Empty;
        existing.LdapBaseDn = request.LdapBaseDn ?? string.Empty;
        existing.LdapUserFilter = request.LdapUserFilter ?? string.Empty;
        existing.LdapGroupFilter = request.LdapGroupFilter ?? string.Empty;
        // Bind password is write-only: encrypt + store only when a new one is supplied; otherwise keep existing.
        if (!string.IsNullOrEmpty(request.LdapBindPassword))
            existing.LdapBindPasswordEncrypted = secretProtector.Protect(request.TenantId, request.LdapBindPassword);

        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = DirectorySyncConfigResponse.From(existing);
        return isNew
            ? Results.Created("/api/admin/directory/config", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetConfigAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.IntegrationsRead, tenantId, out var fail))
            return fail!;
        var config = await dbContext.TenantDirectorySyncConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        return config == null ? Results.NotFound() : Results.Ok(DirectorySyncConfigResponse.From(config));
    }

    private static async Task<IResult> TriggerSyncAsync(
        TriggerSyncRequest request,
        HttpContext httpContext,
        IDirectorySyncProviderFactory providerFactory,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.IntegrationsWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        // Resolve the provider per-tenant AFTER reading config (not via a single DI registration).
        var config = await dbContext.TenantDirectorySyncConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);
        if (config is null)
            return Results.NotFound();

        IDirectorySyncService syncService;
        try
        {
            syncService = providerFactory.For(config.Provider);
        }
        catch (DirectorySyncProviderUnavailableException ex)
        {
            return Results.Json(new ErrorResponse($"provider_unavailable:{ex.Provider}"), statusCode: 501);
        }

        try
        {
            var result = await syncService.SyncAsync(request.TenantId, cancellationToken);
            return Results.Ok(new SyncResultResponse(result.UsersUpserted, result.GroupsUpserted, result.MembershipsUpserted));
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
    }

    private sealed record DirectorySyncConfigRequest(
        Guid TenantId,
        string EntraTenantId,
        string ClientId,
        string ClientSecret,
        string? Provider = null,
        string? LdapHost = null,
        int? LdapPort = null,
        bool? LdapUseLdaps = null,
        string? LdapBindDn = null,
        string? LdapBindPassword = null,
        string? LdapBaseDn = null,
        string? LdapUserFilter = null,
        string? LdapGroupFilter = null);

    private sealed record TriggerSyncRequest(Guid TenantId);

    private sealed record DirectorySyncConfigResponse(
        Guid TenantId,
        string EntraTenantId,
        string ClientId,
        string Provider,
        string LdapHost,
        int LdapPort,
        bool LdapUseLdaps,
        string LdapBindDn,
        bool LdapBindPasswordSet,
        string LdapBaseDn,
        string LdapUserFilter,
        string LdapGroupFilter,
        DateTimeOffset? LastSyncAtUtc,
        string? LastSyncStatus,
        int? LastSyncUserCount,
        int? LastSyncGroupCount)
    {
        // Note: ClientSecret and the LDAP bind password are never returned (write-only).
        public static DirectorySyncConfigResponse From(TenantDirectorySyncConfigEntity c)
            => new(c.TenantId, c.EntraTenantId, c.ClientId,
                   c.Provider, c.LdapHost, c.LdapPort, c.LdapUseLdaps, c.LdapBindDn,
                   !string.IsNullOrEmpty(c.LdapBindPasswordEncrypted), c.LdapBaseDn, c.LdapUserFilter, c.LdapGroupFilter,
                   c.LastSyncAtUtc, c.LastSyncStatus, c.LastSyncUserCount, c.LastSyncGroupCount);
    }

    private sealed record SyncResultResponse(int UsersUpserted, int GroupsUpserted, int MembershipsUpserted);

    private sealed record ErrorResponse(string ReasonCode);
}
