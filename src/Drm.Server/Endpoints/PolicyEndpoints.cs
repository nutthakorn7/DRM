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
        CancellationToken cancellationToken)
    {
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
