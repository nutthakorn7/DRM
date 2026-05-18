using Microsoft.AspNetCore.Http.HttpResults;

namespace Drm.Server.Endpoints;

public static class AdminPolicySimulatorEndpoints
{
    public static IEndpointRouteBuilder MapAdminPolicySimulatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/policy-simulator", SimulateAsync);

        return endpoints;
    }

    private static async Task<IResult> SimulateAsync(
        PolicySimulationRequest request,
        HttpContext httpContext,
        PolicyDecisionService policyDecisionService,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.PoliciesRead, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

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

        if (decision.InvalidPermission) return Results.BadRequest(response);
        if (!decision.FileFound) return Results.NotFound(response);
        return Results.Ok(response);
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

    private sealed record ErrorResponse(string ReasonCode);
}
