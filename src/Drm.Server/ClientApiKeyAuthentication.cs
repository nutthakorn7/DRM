using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace Drm.Server;

public static class ClientApiKeyAuthentication
{
    public const string HeaderName = "X-DRM-Client-Key";

    // Items key used to expose the resolved tenant when a per-tenant key matched
    public const string ResolvedTenantItemKey = "DrmClientKeyTenant";

    public static IApplicationBuilder UseClientApiKeyAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/share-links", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var submittedKeyValues) ||
                StringValues.IsNullOrEmpty(submittedKeyValues))
            {
                // Only reject if at least one auth mechanism is configured
                var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
                var configuredKey = configuration["Drm:Security:ClientApiKey"];
                var db = context.RequestServices.GetRequiredService<AppDbContext>();
                var anyTenantKeys = await db.TenantClientKeys.AnyAsync(k => !k.Revoked, context.RequestAborted);

                if (!string.IsNullOrWhiteSpace(configuredKey) || anyTenantKeys)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse("client_api_key_required"));
                    return;
                }

                await next(context);
                return;
            }

            var submittedKey = submittedKeyValues.ToString();

            // 1. Try per-tenant key lookup (SHA-256 hash comparison)
            if (submittedKey.StartsWith(TenantClientKeyCrypto.KeyPrefix, StringComparison.Ordinal))
            {
                var keyHash = TenantClientKeyCrypto.HashKey(submittedKey);
                var db = context.RequestServices.GetRequiredService<AppDbContext>();
                var tenantKey = await db.TenantClientKeys
                    .AsNoTracking()
                    .Where(k => k.KeyHash == keyHash && !k.Revoked)
                    .Select(k => new { k.TenantId, k.KeyId })
                    .FirstOrDefaultAsync(context.RequestAborted);

                if (tenantKey is null)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse("client_api_key_invalid"));
                    return;
                }

                // Store resolved tenant so TenantHeaderContext can use it
                context.Items[ResolvedTenantItemKey] = tenantKey.TenantId;
                // Update LastUsedAtUtc in background (fire and forget; non-critical)
                _ = UpdateLastUsedAsync(db, tenantKey.TenantId, tenantKey.KeyId);
                await next(context);
                return;
            }

            // 2. Fall back to global shared key
            {
                var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
                var configuredKey = configuration["Drm:Security:ClientApiKey"];

                if (!string.IsNullOrWhiteSpace(configuredKey))
                {
                    if (!GlobalKeysMatch(configuredKey, submittedKey))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse("client_api_key_invalid"));
                        return;
                    }

                    await next(context);
                    return;
                }
            }

            // No matching mechanism found
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ErrorResponse("client_api_key_invalid"));
        });
    }

    private static bool GlobalKeysMatch(string configuredKey, string submittedKey)
    {
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var submittedBytes = Encoding.UTF8.GetBytes(submittedKey);
        return configuredBytes.Length == submittedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(configuredBytes, submittedBytes);
    }

    private static async Task UpdateLastUsedAsync(AppDbContext db, Guid tenantId, Guid keyId)
    {
        try
        {
            await db.TenantClientKeys
                .Where(k => k.TenantId == tenantId && k.KeyId == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAtUtc, DateTimeOffset.UtcNow));
        }
        catch
        {
            // best-effort
        }
    }

    private sealed record ErrorResponse(string ReasonCode);
}

public static class TenantClientKeyCrypto
{
    public const string KeyPrefix = "drm_tk_";

    public static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return KeyPrefix + Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string HashKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
