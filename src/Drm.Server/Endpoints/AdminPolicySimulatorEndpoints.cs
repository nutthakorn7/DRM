using Microsoft.AspNetCore.Http.HttpResults;

namespace Drm.Server.Endpoints;

public static class AdminPolicySimulatorEndpoints
{
    public static IEndpointRouteBuilder MapAdminPolicySimulatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/policy-simulator", SimulateAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<PolicySimulationResponse>, NotFound<PolicySimulationResponse>, BadRequest<PolicySimulationResponse>>> SimulateAsync(
        PolicySimulationRequest request,
        PolicyDecisionService policyDecisionService,
        CancellationToken cancellationToken)
    {
        var decision = await policyDecisionService.SimulateAsync(
            request.TenantId,
            request.FileId,
            request.UserId,
            request.DeviceId,
            request.RequestedPermission,
            cancellationToken);

        var response = new PolicySimulationResponse(
            decision.Allowed,
            decision.AllowedPermissions.ToString(),
            decision.ReasonCode,
            decision.WatermarkTemplate,
            decision.OfflineLeaseExpiresAtUtc,
            Simulated: true);

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

    private sealed record PolicySimulationRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission);

    private sealed record PolicySimulationResponse(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc,
        bool Simulated);
}
