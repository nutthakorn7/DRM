using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminPolicyTemplatesEndpoints
{
    private const int MaxNameLength = 256;
    private const int MaxWatermarkTemplateLength = 1024;
    private const int MaxOfflineLeaseMinutes = 366 * 24 * 60;

    public static IEndpointRouteBuilder MapAdminPolicyTemplatesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/policy-templates");

        group.MapPost("/", CreatePolicyTemplateAsync);
        group.MapGet("/", ListPolicyTemplatesAsync);
        group.MapGet("/{templateId:guid}", GetPolicyTemplateAsync);

        return endpoints;
    }

    private static async Task<IResult> CreatePolicyTemplateAsync(
        CreatePolicyTemplateRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.PoliciesWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var validationError = ValidateCreateRequest(request);
        if (validationError is not null) return Results.BadRequest(validationError);
        if (!PermissionParser.TryParse(request.Permissions, out var permissions))
            return Results.BadRequest(new ErrorResponse("invalid_permissions"));
        if (await PolicyTemplateExistsAsync(dbContext, request.TenantId, request.TemplateId, cancellationToken))
            return Results.Conflict();

        var template = new PolicyTemplateEntity
        {
            TenantId = request.TenantId,
            TemplateId = request.TemplateId,
            Name = request.Name,
            Permissions = permissions.ToString(),
            WatermarkTemplate = request.WatermarkTemplate,
            OfflineLeaseMinutes = request.OfflineLeaseMinutes,
            AllowPrint = request.AllowPrint,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.PolicyTemplates.Add(template);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "policy_template_created", httpContext));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await PolicyTemplateExistsAsync(dbContext, request.TenantId, request.TemplateId, cancellationToken))
                return Results.Conflict();
            throw;
        }

        return Results.Created(
            $"/api/admin/policy-templates/{template.TemplateId}?tenantId={template.TenantId}",
            PolicyTemplateResponse.From(template));
    }

    private static async Task<IResult> ListPolicyTemplatesAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.PoliciesRead, out var fail))
            return fail!;
        var templates = await dbContext.PolicyTemplates
            .AsNoTracking()
            .Where(template => template.TenantId == tenantId)
            .OrderBy(template => template.Name)
            .Select(template => PolicyTemplateResponse.From(template))
            .ToListAsync(cancellationToken);
        return Results.Ok(templates);
    }

    private static async Task<IResult> GetPolicyTemplateAsync(
        Guid templateId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.PoliciesRead, out var fail))
            return fail!;
        var template = await dbContext.PolicyTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == tenantId && candidate.TemplateId == templateId,
                cancellationToken);
        return template is null ? Results.NotFound() : Results.Ok(PolicyTemplateResponse.From(template));
    }

    private static Task<bool> PolicyTemplateExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        return dbContext.PolicyTemplates
            .AsNoTracking()
            .AnyAsync(template => template.TenantId == tenantId && template.TemplateId == templateId, cancellationToken);
    }

    private static ErrorResponse? ValidateCreateRequest(CreatePolicyTemplateRequest request)
    {
        if (request.TenantId == Guid.Empty)
        {
            return new ErrorResponse("invalid_tenant_id");
        }

        if (request.TemplateId == Guid.Empty)
        {
            return new ErrorResponse("invalid_template_id");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
        {
            return new ErrorResponse("invalid_name");
        }

        if (string.IsNullOrWhiteSpace(request.WatermarkTemplate)
            || request.WatermarkTemplate.Length > MaxWatermarkTemplateLength)
        {
            return new ErrorResponse("invalid_watermark_template");
        }

        if (request.OfflineLeaseMinutes < 0 || request.OfflineLeaseMinutes > MaxOfflineLeaseMinutes)
        {
            return new ErrorResponse("invalid_offline_lease_minutes");
        }

        return null;
    }

    private sealed record CreatePolicyTemplateRequest(
        Guid TenantId,
        Guid TemplateId,
        string Name,
        string Permissions,
        string WatermarkTemplate,
        int OfflineLeaseMinutes,
        bool AllowPrint);

    private sealed record PolicyTemplateResponse(
        Guid TenantId,
        Guid TemplateId,
        string Name,
        string Permissions,
        string WatermarkTemplate,
        int OfflineLeaseMinutes,
        bool AllowPrint)
    {
        public static PolicyTemplateResponse From(PolicyTemplateEntity template)
            => new(
                template.TenantId,
                template.TemplateId,
                template.Name,
                template.Permissions,
                template.WatermarkTemplate,
                template.OfflineLeaseMinutes,
                template.AllowPrint);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
