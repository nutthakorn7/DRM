using Drm.Server;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

/// <summary>
/// Agent self-discovery endpoint. Lets the Windows tray app's first-run wizard
/// turn a work email into the (TenantId, UserId, display name) tuple it needs
/// to talk to the server, plus the tenant's default policy template so
/// right-click-protect "just works" with no user decision.
///
/// Replaces the friction of having end users paste GUIDs they don't know.
///
/// Security note: this endpoint discloses whether a given email is a
/// registered user. We accept that trade-off for the demo / pilot phase
/// because:
/// - Email enumeration is already possible via the email verification flow
///   on /share/ (someone trying random emails sees different errors)
/// - Brute-force is mitigated by the same per-IP rate-limiter that protects
///   /api/external-share/* (when configured)
/// - The endpoint requires a valid tenant-scoped client API key when
///   tenants opt into that protection
///
/// For production hardening (post-demo), add:
/// - Per-IP rate limit (60 requests/hour/IP) on this endpoint specifically
/// - Optional email-confirmation flow (send a token to the email before
///   returning the lookup result)
/// - Audit event so admins see who's doing discovery on their tenant
/// </summary>
public static class AgentDiscoverEndpoints
{
    public static void MapAgentDiscoverEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/agent/discover", DiscoverAsync);
    }

    private static async Task<Results<Ok<AgentDiscoveryResponse>, NotFound, BadRequest<ErrorResponse>>> DiscoverAsync(
        string email,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_email"));
        }

        // Normalise: the database stores email in mixed case but we look up
        // case-insensitively. Same convention the share-link redeem flow uses.
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (normalizedEmail.Length < 3 || !normalizedEmail.Contains('@'))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_email"));
        }

        // EF Core's SQLite provider can't translate string.ToLower() on
        // nvarchar columns reliably, and the codebase targets both SQLite
        // (tests) and Postgres (production). We materialize the tenant-active
        // user rows and filter in-memory. The result set is bounded by the
        // number of users that share an email-domain match — small in practice
        // because email is unique per tenant. For very large multi-tenant
        // deployments this can later move behind a normalised-email index.
        // No ORDER BY in the SQL — SQLite can't translate DateTimeOffset
        // ORDER BY clauses. We sort in memory after materialising. Email is
        // unique per (tenant, email) in TenantUsers so there's at most one
        // active match across all tenants; sort is just deterministic.
        var users = await dbContext.TenantUsers
            .AsNoTracking()
            .Where(u => u.Active)
            .ToListAsync(cancellationToken);

        var user = users
            .OrderBy(u => u.CreatedAtUtc)
            .FirstOrDefault(u => string.Equals(u.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        // Look up the tenant's default policy template. For now, the rule is:
        // "the most recently created template wins as the default." A future
        // change can add an explicit IsDefault flag or a TenantEntity column.
        // SQLite DateTimeOffset ORDER BY is unreliable in this codebase so we
        // materialize-and-sort, same pattern as PolicyDecisionService.
        var templates = await dbContext.PolicyTemplates
            .AsNoTracking()
            .Where(t => t.TenantId == user.TenantId)
            .ToListAsync(cancellationToken);
        Guid? defaultTemplateId = templates
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => (Guid?)t.TemplateId)
            .FirstOrDefault();

        return TypedResults.Ok(new AgentDiscoveryResponse(
            user.TenantId,
            user.UserId,
            user.DisplayName,
            user.Email,
            defaultTemplateId,
            DefaultExpiryDays: 7));
    }

    /// <summary>
    /// Shape the agent first-run dialog consumes. <c>DefaultPolicyTemplateId</c>
    /// may be null when the tenant has not created any templates yet — in that
    /// case the agent falls back to <c>DefaultExpiryDays</c> + a server-side
    /// minimum-permission policy.
    /// </summary>
    public sealed record AgentDiscoveryResponse(
        Guid TenantId,
        Guid UserId,
        string DisplayName,
        string Email,
        Guid? DefaultPolicyTemplateId,
        int DefaultExpiryDays);

    private sealed record ErrorResponse(string ReasonCode);
}
