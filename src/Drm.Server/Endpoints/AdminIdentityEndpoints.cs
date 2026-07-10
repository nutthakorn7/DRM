using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/identity/whoami", WhoAmI);
        endpoints.MapGet("/api/admin/identity/roles", ListRoles);
        endpoints.MapGet("/api/admin/identity/admins", ListAdmins);
        endpoints.MapPost("/api/admin/identity/admins", CreateAdmin);
        endpoints.MapPost("/api/admin/identity/admins/{adminId:guid}/disable", DisableAdmin);
        endpoints.MapPost("/api/admin/identity/admins/{adminId:guid}/enable", EnableAdmin);
        endpoints.MapPost("/api/admin/identity/admins/{adminId:guid}/rotate-token", RotateToken);
        endpoints.MapGet("/api/admin/identity/admins/{adminId:guid}/tokens", ListTokens);
        endpoints.MapPost("/api/admin/identity/tokens/{tokenId:guid}/revoke", RevokeToken);
        return endpoints;
    }

    // Per-token inventory so a compromised credential can actually be found
    // and killed from the console — previously the UI only showed an active
    // *count*, and /tokens/{tokenId}/revoke had no caller anywhere in it.
    private static async Task<Results<Ok<IReadOnlyList<TokenResponse>>, JsonHttpResult<object>>> ListTokens(
        Guid adminId, HttpContext httpContext, AppDbContext db, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsRead, out var fail))
            return Forbidden(fail!);

        // SQLite can't translate ORDER BY on a DateTimeOffset column — sort
        // client-side (a token count of dozens at most, so no scale concern).
        var tokens = await db.AdminApiTokens.AsNoTracking()
            .Where(t => t.AdminUserId == adminId)
            .ToListAsync(ct);

        IReadOnlyList<TokenResponse> response = tokens
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new TokenResponse(t.Id, t.Label, t.CreatedAtUtc, t.ExpiresAtUtc, t.LastUsedAtUtc, t.Revoked))
            .ToArray();
        return TypedResults.Ok(response);
    }

    private static Results<Ok<WhoAmIResponse>, JsonHttpResult<object>> WhoAmI(HttpContext httpContext)
    {
        var identity = AdminIdentityContext.Current(httpContext);
        if (identity is null) return Unauthorized();
        return TypedResults.Ok(new WhoAmIResponse(
            identity.AdminUserId,
            identity.RoleId,
            identity.DisplayName,
            identity.Email,
            identity.Permissions.ToArray(),
            identity.IsSharedKeyFallback,
            identity.TenantScope));
    }

    private static async Task<Results<Ok<IReadOnlyList<RoleResponse>>, JsonHttpResult<object>>> ListRoles(
        HttpContext httpContext, AppDbContext db, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.RolesRead, out var fail))
            return Forbidden(fail!);

        var rows = await db.AdminRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        IReadOnlyList<RoleResponse> response = rows
            .Select(r => new RoleResponse(r.Id, r.Name, r.PermissionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), r.IsSystem))
            .ToArray();
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<IReadOnlyList<AdminResponse>>, JsonHttpResult<object>>> ListAdmins(
        HttpContext httpContext, AppDbContext db, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsRead, out var fail))
            return Forbidden(fail!);

        var admins = await db.AdminUsers.AsNoTracking().OrderBy(a => a.Email).ToListAsync(ct);
        var roles = await db.AdminRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
        var tokensByUser = await db.AdminApiTokens.AsNoTracking()
            .GroupBy(t => t.AdminUserId)
            .Select(g => new { UserId = g.Key, Active = g.Count(t => !t.Revoked) })
            .ToDictionaryAsync(x => x.UserId, x => x.Active, ct);

        IReadOnlyList<AdminResponse> response = admins.Select(a => new AdminResponse(
            a.Id,
            a.Email,
            a.DisplayName,
            a.RoleId,
            roles.TryGetValue(a.RoleId, out var role) ? role.Name : "(unknown)",
            a.Disabled,
            a.CreatedAtUtc,
            a.LastUsedAtUtc,
            tokensByUser.TryGetValue(a.Id, out var count) ? count : 0,
            a.TenantScope)).ToArray();
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<CreateAdminResponse>, JsonHttpResult<object>>> CreateAdmin(
        HttpContext httpContext, AppDbContext db, CreateAdminRequest request, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsWrite, out var fail))
            return Forbidden(fail!);

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("email_required");
        if (request.RoleId == Guid.Empty)
            return BadRequest("role_required");

        var role = await db.AdminRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);
        if (role is null) return BadRequest("role_not_found");

        var emailExists = await db.AdminUsers.AsNoTracking().AnyAsync(u => u.Email == request.Email, ct);
        if (emailExists) return BadRequest("email_already_exists");

        var now = DateTimeOffset.UtcNow;
        var admin = new AdminUserEntity
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName?.Trim() ?? string.Empty,
            RoleId = request.RoleId,
            Disabled = false,
            CreatedAtUtc = now,
            TenantScope = request.TenantScope,
        };
        db.AdminUsers.Add(admin);

        var plaintextToken = AdminTokenCrypto.GenerateToken();
        var token = new AdminApiTokenEntity
        {
            Id = Guid.NewGuid(),
            AdminUserId = admin.Id,
            TokenHash = AdminTokenCrypto.HashToken(plaintextToken),
            Label = request.TokenLabel?.Trim() ?? "initial",
            CreatedAtUtc = now,
            ExpiresAtUtc = request.TokenExpiresAtUtc,
        };
        db.AdminApiTokens.Add(token);

        await db.SaveChangesAsync(ct);

        WriteAudit(db, httpContext, "admin_created", admin.Id);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateAdminResponse(
            admin.Id,
            admin.Email,
            admin.DisplayName,
            admin.RoleId,
            role.Name,
            token.Id,
            plaintextToken,
            token.ExpiresAtUtc,
            admin.TenantScope));
    }

    private static async Task<Results<Ok<object>, JsonHttpResult<object>>> DisableAdmin(
        Guid adminId, HttpContext httpContext, AppDbContext db, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsWrite, out var fail))
            return Forbidden(fail!);

        if (adminId == AdminSystemRoles.DefaultSuperAdminUserId)
            return BadRequest("cannot_disable_default_super_admin");

        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == adminId, ct);
        if (admin is null) return BadRequest("admin_not_found");

        admin.Disabled = true;
        await db.AdminApiTokens.Where(t => t.AdminUserId == adminId && !t.Revoked).ForEachAsync(t => t.Revoked = true, ct);
        WriteAudit(db, httpContext, "admin_disabled", adminId);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok<object>(new { ok = true });
    }

    private static async Task<Results<Ok<object>, JsonHttpResult<object>>> EnableAdmin(
        Guid adminId, HttpContext httpContext, AppDbContext db, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsWrite, out var fail))
            return Forbidden(fail!);

        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == adminId, ct);
        if (admin is null) return BadRequest("admin_not_found");

        admin.Disabled = false;
        WriteAudit(db, httpContext, "admin_enabled", adminId);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok<object>(new { ok = true });
    }

    private static async Task<Results<Ok<RotateTokenResponse>, JsonHttpResult<object>>> RotateToken(
        Guid adminId, HttpContext httpContext, AppDbContext db, RotateTokenRequest? request, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsWrite, out var fail))
            return Forbidden(fail!);

        var admin = await db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == adminId, ct);
        if (admin is null) return BadRequest("admin_not_found");

        // The Okta/Stripe roll-key contract: rotating mints a new token AND
        // kills every predecessor immediately. Previously this only ever
        // added a new row — every prior token an admin had ever been issued
        // stayed valid forever, so "rotate" didn't actually roll anything.
        var priorActiveTokens = await db.AdminApiTokens
            .Where(t => t.AdminUserId == adminId && !t.Revoked)
            .ToListAsync(ct);
        foreach (var prior in priorActiveTokens)
        {
            prior.Revoked = true;
        }

        var plaintext = AdminTokenCrypto.GenerateToken();
        var now = DateTimeOffset.UtcNow;
        var token = new AdminApiTokenEntity
        {
            Id = Guid.NewGuid(),
            AdminUserId = adminId,
            TokenHash = AdminTokenCrypto.HashToken(plaintext),
            Label = request?.Label?.Trim() ?? "rotated",
            CreatedAtUtc = now,
            ExpiresAtUtc = request?.ExpiresAtUtc,
        };
        db.AdminApiTokens.Add(token);
        WriteAudit(db, httpContext, "admin_token_rotated", adminId);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new RotateTokenResponse(token.Id, plaintext, token.ExpiresAtUtc));
    }

    private static async Task<Results<Ok<object>, JsonHttpResult<object>>> RevokeToken(
        Guid tokenId, HttpContext httpContext, AppDbContext db, CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.AdminsWrite, out var fail))
            return Forbidden(fail!);

        var token = await db.AdminApiTokens.FirstOrDefaultAsync(t => t.Id == tokenId, ct);
        if (token is null) return BadRequest("token_not_found");

        token.Revoked = true;
        WriteAudit(db, httpContext, "admin_token_revoked", token.AdminUserId);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok<object>(new { ok = true });
    }

    private static void WriteAudit(AppDbContext db, HttpContext httpContext, string eventType, Guid targetId)
    {
        var identity = AdminIdentityContext.Current(httpContext);
        db.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = Guid.Empty,
            FileId = targetId,
            UserId = identity?.AdminUserId,
            EventType = eventType,
            ReasonCode = identity?.IsSharedKeyFallback == true ? "shared_key" : "admin_token",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static JsonHttpResult<object> BadRequest(string reasonCode)
        => TypedResults.Json<object>(new { reasonCode }, statusCode: 400);

    private static JsonHttpResult<object> Unauthorized()
        => TypedResults.Json<object>(new { reasonCode = "admin_identity_missing" }, statusCode: 401);

    private static JsonHttpResult<object> Forbidden(IResult underlying)
    {
        // Underlying is already a Results.Json wrapper from the helper; collapse to our typed shape.
        return TypedResults.Json<object>(new { reasonCode = "permission_denied" }, statusCode: 403);
    }

    private sealed record WhoAmIResponse(
        Guid AdminUserId,
        Guid RoleId,
        string DisplayName,
        string Email,
        IReadOnlyList<string> Permissions,
        bool SharedKeyFallback,
        Guid? TenantScope);

    private sealed record RoleResponse(Guid Id, string Name, IReadOnlyList<string> Permissions, bool IsSystem);

    private sealed record AdminResponse(
        Guid Id,
        string Email,
        string DisplayName,
        Guid RoleId,
        string RoleName,
        bool Disabled,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastUsedAtUtc,
        int ActiveTokenCount,
        Guid? TenantScope);

    private sealed record TokenResponse(
        Guid Id,
        string Label,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset? LastUsedAtUtc,
        bool Revoked);

    public sealed record CreateAdminRequest(string Email, string? DisplayName, Guid RoleId, string? TokenLabel, DateTimeOffset? TokenExpiresAtUtc, Guid? TenantScope = null);

    public sealed record CreateAdminResponse(
        Guid AdminUserId,
        string Email,
        string DisplayName,
        Guid RoleId,
        string RoleName,
        Guid TokenId,
        string Token,
        DateTimeOffset? TokenExpiresAtUtc,
        Guid? TenantScope = null);

    public sealed record RotateTokenRequest(string? Label, DateTimeOffset? ExpiresAtUtc);
    public sealed record RotateTokenResponse(Guid TokenId, string Token, DateTimeOffset? ExpiresAtUtc);
}
