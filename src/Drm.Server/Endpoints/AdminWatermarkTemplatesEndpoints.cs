using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminWatermarkTemplatesEndpoints
{
    private const int MaxNameLength = 256;
    private const int MaxPatternLength = 1024;

    public static IEndpointRouteBuilder MapAdminWatermarkTemplatesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/watermark-templates");

        group.MapPost("/", CreateWatermarkTemplateAsync);
        group.MapGet("/", ListWatermarkTemplatesAsync);
        group.MapGet("/{watermarkTemplateId:guid}", GetWatermarkTemplateAsync);

        return endpoints;
    }

    private static async Task<Results<Created<WatermarkTemplateResponse>, Conflict, BadRequest<ErrorResponse>>> CreateWatermarkTemplateAsync(
        CreateWatermarkTemplateRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateCreateRequest(request);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        if (await WatermarkTemplateExistsAsync(
                dbContext,
                request.TenantId,
                request.WatermarkTemplateId,
                cancellationToken))
        {
            return TypedResults.Conflict();
        }

        var template = new WatermarkTemplateEntity
        {
            TenantId = request.TenantId,
            WatermarkTemplateId = request.WatermarkTemplateId,
            Name = request.Name.Trim(),
            Pattern = request.Pattern.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.WatermarkTemplates.Add(template);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "watermark_template_created"));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await WatermarkTemplateExistsAsync(
                    dbContext,
                    request.TenantId,
                    request.WatermarkTemplateId,
                    cancellationToken))
            {
                return TypedResults.Conflict();
            }

            throw;
        }

        return TypedResults.Created(
            $"/api/admin/watermark-templates/{template.WatermarkTemplateId}?tenantId={template.TenantId}",
            WatermarkTemplateResponse.From(template));
    }

    private static async Task<IReadOnlyList<WatermarkTemplateResponse>> ListWatermarkTemplatesAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.WatermarkTemplates
            .AsNoTracking()
            .Where(template => template.TenantId == tenantId)
            .OrderBy(template => template.Name)
            .ThenBy(template => template.WatermarkTemplateId)
            .Select(template => WatermarkTemplateResponse.From(template))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Ok<WatermarkTemplateResponse>, NotFound>> GetWatermarkTemplateAsync(
        Guid watermarkTemplateId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.WatermarkTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == tenantId
                    && candidate.WatermarkTemplateId == watermarkTemplateId,
                cancellationToken);

        if (template is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(WatermarkTemplateResponse.From(template));
    }

    private static Task<bool> WatermarkTemplateExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid watermarkTemplateId,
        CancellationToken cancellationToken)
    {
        return dbContext.WatermarkTemplates
            .AsNoTracking()
            .AnyAsync(
                template => template.TenantId == tenantId
                    && template.WatermarkTemplateId == watermarkTemplateId,
                cancellationToken);
    }

    private static ErrorResponse? ValidateCreateRequest(CreateWatermarkTemplateRequest request)
    {
        if (request.TenantId == Guid.Empty)
        {
            return new ErrorResponse("invalid_tenant_id");
        }

        if (request.WatermarkTemplateId == Guid.Empty)
        {
            return new ErrorResponse("invalid_watermark_template_id");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
        {
            return new ErrorResponse("invalid_name");
        }

        if (string.IsNullOrWhiteSpace(request.Pattern) || request.Pattern.Length > MaxPatternLength)
        {
            return new ErrorResponse("invalid_pattern");
        }

        return null;
    }

    private sealed record CreateWatermarkTemplateRequest(
        Guid TenantId,
        Guid WatermarkTemplateId,
        string Name,
        string Pattern);

    private sealed record WatermarkTemplateResponse(
        Guid TenantId,
        Guid WatermarkTemplateId,
        string Name,
        string Pattern,
        DateTimeOffset CreatedAtUtc)
    {
        public static WatermarkTemplateResponse From(WatermarkTemplateEntity template)
            => new(
                template.TenantId,
                template.WatermarkTemplateId,
                template.Name,
                template.Pattern,
                template.CreatedAtUtc);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
