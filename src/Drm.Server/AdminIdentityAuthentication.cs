using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace Drm.Server;

// Replaces the old AdminApiKeyAuthentication middleware. Resolves an
// AdminIdentity for every /api/admin/* request from one of:
//   1. X-DRM-Admin-Token header (per-admin token, new in v1.1)
//   2. X-DRM-Admin-Key header (shared key, v1.0.x back-compat)
//
// Either path stamps HttpContext.Items[AdminIdentityContext.ContextKey] with
// the resolved identity, so endpoints downstream get a real userId + role +
// permission set regardless of which auth surface the client used.
public static class AdminIdentityAuthentication
{
    public const string TokenHeaderName = "X-DRM-Admin-Token";
    public const string SharedKeyHeaderName = "X-DRM-Admin-Key";

    public static IApplicationBuilder UseAdminIdentityAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
            var dbContext = context.RequestServices.GetRequiredService<AppDbContext>();

            // Per-admin token wins when present — it carries identity.
            if (context.Request.Headers.TryGetValue(TokenHeaderName, out var submittedToken)
                && !StringValues.IsNullOrEmpty(submittedToken))
            {
                var identity = await ResolveByTokenAsync(dbContext, submittedToken.ToString()!, context.RequestAborted);
                if (identity is null)
                {
                    await WriteError(context, 403, "admin_token_invalid");
                    return;
                }
                context.Items[AdminIdentityContext.ContextKey] = identity;
                await next(context);
                return;
            }

            var configuredKey = configuration["Drm:Security:AdminApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                if (!environment.IsDevelopment())
                {
                    await WriteError(context, 503, "admin_api_key_unconfigured");
                    return;
                }
                // Development: skip auth entirely. Endpoints that read
                // AdminIdentity will see null and behave as anonymous.
                await next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(SharedKeyHeaderName, out var submittedKey)
                || StringValues.IsNullOrEmpty(submittedKey))
            {
                await WriteError(context, 401, "admin_credential_required");
                return;
            }

            if (!KeysMatch(configuredKey, submittedKey.ToString()))
            {
                await WriteError(context, 403, "admin_api_key_invalid");
                return;
            }

            // Shared-key path: synthesize the default-SuperAdmin identity so
            // downstream RBAC checks and audit attribution still work.
            context.Items[AdminIdentityContext.ContextKey] = await ResolveSharedKeyIdentityAsync(dbContext, context.RequestAborted);
            await next(context);
        });
    }

    private static async Task<AdminIdentity?> ResolveByTokenAsync(
        AppDbContext dbContext, string submittedToken, CancellationToken cancellationToken)
    {
        if (!submittedToken.StartsWith(AdminTokenCrypto.TokenPrefix, StringComparison.Ordinal))
            return null;

        var hash = AdminTokenCrypto.HashToken(submittedToken);
        var tokenRow = await dbContext.AdminApiTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (tokenRow is null || tokenRow.Revoked) return null;
        if (tokenRow.ExpiresAtUtc.HasValue && tokenRow.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow) return null;

        var user = await dbContext.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tokenRow.AdminUserId, cancellationToken);
        if (user is null || user.Disabled) return null;

        var role = await dbContext.AdminRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        if (role is null) return null;

        // Touch last-used for both token and user. Fire-and-forget on a separate
        // tracking context to avoid the AsNoTracking pattern above.
        var trackedToken = await dbContext.AdminApiTokens.FirstOrDefaultAsync(t => t.Id == tokenRow.Id, cancellationToken);
        var trackedUser = await dbContext.AdminUsers.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        if (trackedToken is not null) trackedToken.LastUsedAtUtc = DateTimeOffset.UtcNow;
        if (trackedUser is not null) trackedUser.LastUsedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AdminIdentity(
            AdminUserId: user.Id,
            RoleId: role.Id,
            DisplayName: user.DisplayName,
            Email: user.Email,
            Permissions: ParsePermissions(role.PermissionsCsv),
            IsSharedKeyFallback: false);
    }

    private static async Task<AdminIdentity> ResolveSharedKeyIdentityAsync(
        AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var role = await dbContext.AdminRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == AdminSystemRoles.SuperAdminId, cancellationToken);
        var permissions = role is not null
            ? ParsePermissions(role.PermissionsCsv)
            : new HashSet<string> { AdminPermissions.Wildcard };

        return new AdminIdentity(
            AdminUserId: AdminSystemRoles.DefaultSuperAdminUserId,
            RoleId: AdminSystemRoles.SuperAdminId,
            DisplayName: AdminSystemRoles.DefaultSuperAdminDisplayName,
            Email: AdminSystemRoles.DefaultSuperAdminEmail,
            Permissions: permissions,
            IsSharedKeyFallback: true);
    }

    private static IReadOnlySet<string> ParsePermissions(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new HashSet<string>();
        return new HashSet<string>(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
    }

    private static bool KeysMatch(string configuredKey, string submittedKey)
    {
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var submittedBytes = Encoding.UTF8.GetBytes(submittedKey);
        return configuredBytes.Length == submittedBytes.Length
            && CryptographicOperations.FixedTimeEquals(configuredBytes, submittedBytes);
    }

    private static Task WriteError(HttpContext context, int status, string reasonCode)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { reasonCode });
    }
}
