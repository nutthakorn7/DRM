using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminTenantClientKeysEndpoints
{
    public static IEndpointRouteBuilder MapAdminTenantClientKeysEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/tenants/{tenantId:guid}/client-keys");

        group.MapGet("/", ListKeysAsync);
        group.MapPost("/", CreateKeyAsync);
        group.MapDelete("/{keyId:guid}", RevokeKeyAsync);

        return endpoints;
    }

    private static async Task<IResult> ListKeysAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        if (!await TenantExistsAsync(dbContext, tenantId, cancellationToken))
            return Results.NotFound();

        var keys = await dbContext.TenantClientKeys
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId && !k.Revoked)
            .OrderBy(k => k.KeyId)
            .Select(k => KeyResponse.From(k))
            .ToListAsync(cancellationToken);

        return Results.Ok(keys);
    }

    private static async Task<IResult> CreateKeyAsync(
        Guid tenantId,
        CreateKeyRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        if (!await TenantExistsAsync(dbContext, tenantId, cancellationToken))
            return Results.NotFound();

        var rawKey = TenantClientKeyCrypto.GenerateKey();
        var keyHash = TenantClientKeyCrypto.HashKey(rawKey);

        var entity = new TenantClientKeyEntity
        {
            TenantId = tenantId,
            KeyId = Guid.NewGuid(),
            KeyHash = keyHash,
            Label = request.Label ?? string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantClientKeys.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/admin/tenants/{tenantId}/client-keys/{entity.KeyId}",
            new CreateKeyResponse(entity.KeyId, tenantId, entity.Label, rawKey, entity.CreatedAtUtc));
    }

    private static async Task<IResult> RevokeKeyAsync(
        Guid tenantId,
        Guid keyId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var key = await dbContext.TenantClientKeys
            .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.KeyId == keyId, cancellationToken);

        if (key is null || key.Revoked)
            return Results.NotFound();

        key.Revoked = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static Task<bool> TenantExistsAsync(AppDbContext dbContext, Guid tenantId, CancellationToken ct)
        => dbContext.Tenants.AnyAsync(t => t.TenantId == tenantId, ct);

    private sealed record CreateKeyRequest(string? Label);

    private sealed record KeyResponse(Guid KeyId, Guid TenantId, string Label, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc)
    {
        public static KeyResponse From(TenantClientKeyEntity k)
            => new(k.KeyId, k.TenantId, k.Label, k.CreatedAtUtc, k.LastUsedAtUtc);
    }

    private sealed record CreateKeyResponse(Guid KeyId, Guid TenantId, string Label, string Key, DateTimeOffset CreatedAtUtc);
}
