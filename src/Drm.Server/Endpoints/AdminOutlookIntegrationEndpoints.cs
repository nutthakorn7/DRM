using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminOutlookIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapAdminOutlookIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/outlook");

        group.MapPut("/config", UpsertConfigAsync);
        group.MapGet("/config", GetConfigAsync);
        group.MapGet("/events", ListEventsAsync);

        return endpoints;
    }

    private static async Task<IResult> UpsertConfigAsync(
        OutlookConfigRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.IntegrationsWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var existing = await dbContext.TenantOutlookIntegrationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);

        var isNew = existing is null;
        if (existing is null)
        {
            existing = new TenantOutlookIntegrationConfigEntity { TenantId = request.TenantId };
            dbContext.TenantOutlookIntegrationConfigs.Add(existing);
        }

        existing.Enabled = request.Enabled;
        existing.AutoEncryptOutgoingAttachments = request.AutoEncryptOutgoingAttachments;
        existing.MinAttachmentSizeKb = Math.Max(0, request.MinAttachmentSizeKb);
        existing.SkipDomainsCsv = request.SkipDomainsCsv?.Trim() ?? string.Empty;
        existing.DefaultPolicyTemplateId = string.IsNullOrWhiteSpace(request.DefaultPolicyTemplateId)
            ? null
            : request.DefaultPolicyTemplateId.Trim();
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = OutlookConfigResponse.From(existing);
        return isNew
            ? Results.Created("/api/admin/outlook/config", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetConfigAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.IntegrationsRead, tenantId, out var fail))
            return fail!;
        var config = await dbContext.TenantOutlookIntegrationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        return config is null ? Results.NotFound() : Results.Ok(OutlookConfigResponse.From(config));
    }

    private static async Task<IResult> ListEventsAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.IntegrationsRead, tenantId, out var fail))
            return fail!;
        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        var events = await dbContext.OutlookAttachmentEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.Id)
            .Take(pageSize)
            .Select(e => new OutlookEventResponse(
                e.Id, e.SenderEmail, e.RecipientCsv, e.AttachmentName,
                e.AttachmentSizeBytes, e.Status, e.ProtectedFileId, e.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        return Results.Ok(events);
    }

    private sealed record OutlookConfigRequest(
        Guid TenantId,
        bool Enabled,
        bool AutoEncryptOutgoingAttachments,
        int MinAttachmentSizeKb,
        string? SkipDomainsCsv,
        string? DefaultPolicyTemplateId);

    private sealed record OutlookConfigResponse(
        Guid TenantId,
        bool Enabled,
        bool AutoEncryptOutgoingAttachments,
        int MinAttachmentSizeKb,
        string SkipDomainsCsv,
        string? DefaultPolicyTemplateId,
        int LifetimeProtectedCount,
        DateTimeOffset UpdatedAtUtc)
    {
        public static OutlookConfigResponse From(TenantOutlookIntegrationConfigEntity c)
            => new(c.TenantId, c.Enabled, c.AutoEncryptOutgoingAttachments, c.MinAttachmentSizeKb,
                   c.SkipDomainsCsv, c.DefaultPolicyTemplateId, c.LifetimeProtectedCount, c.UpdatedAtUtc);
    }

    private sealed record OutlookEventResponse(
        long Id,
        string SenderEmail,
        string RecipientCsv,
        string AttachmentName,
        long AttachmentSizeBytes,
        string Status,
        string? ProtectedFileId,
        DateTimeOffset OccurredAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
