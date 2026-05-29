using Microsoft.AspNetCore.Http.HttpResults;

namespace Drm.Server.Endpoints;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/policy/decide", DecideAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<PolicyDecisionResponse>, NotFound<PolicyDecisionResponse>, BadRequest<PolicyDecisionResponse>>> DecideAsync(
        DecidePolicyRequest request,
        PolicyDecisionService policyDecisionService,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // PR #50 hardening: enforce the tenant IP allowlist here too, matching
        // the unwrap endpoint. Without it a tenant that locked down which IPs
        // may touch protected files could still have its policy / permission /
        // offline-lease state queried from any non-allowlisted IP. Surfaced as
        // a denied decision (allowed=false), consistent with how this endpoint
        // already reports device-trust denials, rather than a 403 throw.
        //
        // NOTE: this endpoint intentionally returns a decision only (no key
        // material) and is NOT device-signature gated. It must therefore never
        // be treated as standalone authorization to open a file — the gated
        // /keys/unwrap path is the authority. Adding a signature requirement
        // here is an API-contract change (callers do not sign decide today)
        // and is left to the author; see the PR follow-up notes.
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        if (await IpAllowlistService.IsDeniedAsync(dbContext, request.TenantId, clientIp, cancellationToken))
        {
            return TypedResults.Ok(new PolicyDecisionResponse(
                false,
                Domain.Permission.None.ToString(),
                "ip_not_allowed",
                null,
                null));
        }

        var decision = await policyDecisionService.DecideAsync(
            request.TenantId,
            request.FileId,
            request.UserId,
            request.DeviceId,
            request.RequestedPermission,
            cancellationToken);

        var response = new PolicyDecisionResponse(
            decision.Allowed,
            decision.AllowedPermissions.ToString(),
            decision.ReasonCode,
            decision.WatermarkTemplate,
            decision.OfflineLeaseExpiresAtUtc);

        if (decision.InvalidPermission)
        {
            return TypedResults.BadRequest(response);
        }

        if (!decision.FileFound)
        {
            return TypedResults.NotFound(response);
        }

        return TypedResults.Ok(response);
    }

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission,
        DateTimeOffset? AtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);
}
