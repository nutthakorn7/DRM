namespace Drm.Server.Endpoints;

public static class AdminAuditChainEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditChainEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit/chain/verify", VerifyAsync);
        return endpoints;
    }

    private static async Task<IResult> VerifyAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.AuditRead, tenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
            return Results.BadRequest(new { reasonCode = "tenant_mismatch" });

        var keyHex = configuration["Drm:Security:AuditChainKey"] ?? string.Empty;
        var result = await AuditChainService.VerifyTenantChainAsync(dbContext, tenantId, keyHex, cancellationToken);

        return Results.Ok(new
        {
            tenantId,
            valid = result.Valid,
            eventsChecked = result.EventsChecked,
            failureReason = result.FailureReason
        });
    }
}
