using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Reads the <c>X-DRM-Tenant-Id</c> request header (if present) and parks it
/// in <see cref="HttpContext.Items"/> so endpoints can cross-check it against
/// whatever tenant identifier the caller passed in the body.
///
/// Also enforces tenant suspension: if the resolved tenant has Status == Suspended,
/// all /api/* requests for that tenant are rejected with 403 tenant_suspended.
///
/// The header is the long-term canonical source of tenant context across all
/// /api/* endpoints. Today, body-side <c>TenantId</c> remains accepted for
/// backwards compatibility; this middleware just lets us assert "header and
/// body agree" without rewriting every endpoint at once.
///
/// See SECURITY.md for the full rationale and migration plan.
/// </summary>
public static class TenantHeaderContext
{
    public const string HeaderName = "X-DRM-Tenant-Id";
    public const string ItemKey = "DrmTenantHeader";

    public static IApplicationBuilder UseTenantHeaderContext(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var raw)
                && Guid.TryParse(raw.ToString(), out var tenantId)
                && tenantId != Guid.Empty)
            {
                context.Items[ItemKey] = tenantId;

                // Enforce suspension only for /api/* paths (skip admin-identity and healthz)
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    var db = context.RequestServices.GetRequiredService<AppDbContext>();
                    var tenant = await db.Tenants
                        .AsNoTracking()
                        .Where(t => t.TenantId == tenantId)
                        .Select(t => new { t.Status })
                        .FirstOrDefaultAsync(context.RequestAborted);

                    if (tenant?.Status == TenantStatus.Suspended)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new { reasonCode = "tenant_suspended" });
                        return;
                    }
                }
            }

            await next(context);
        });
    }

    /// <summary>
    /// Returns the tenant ID asserted via the <c>X-DRM-Tenant-Id</c> header,
    /// or <c>null</c> if the header wasn't present or didn't parse.
    /// </summary>
    public static Guid? HeaderTenantId(this HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var value) && value is Guid id ? id : null;

    /// <summary>
    /// Returns <c>true</c> if a caller-supplied body tenant agrees with the
    /// header (or if the header is absent). Returns <c>false</c> when the
    /// header is present and a different tenant ID is in the body. Endpoints
    /// should treat <c>false</c> as a 400 <c>tenant_mismatch</c>.
    /// </summary>
    public static bool MatchesHeader(this HttpContext context, Guid bodyTenantId)
    {
        var header = context.HeaderTenantId();
        return header is null || header.Value == bodyTenantId;
    }
}
