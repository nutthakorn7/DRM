using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminWatermarkTemplatesEndpoints
{
    private const int MaxNameLength = 256;
    private const int MaxPatternLength = 1024;
    private const int MinOpacityPercent = 5;
    private const int MaxOpacityPercent = 100;
    private const int MinDensityTiles = 1;
    private const int MaxDensityTiles = 12;
    private const int MinAngleDegrees = -90;
    private const int MaxAngleDegrees = 90;

    public static IEndpointRouteBuilder MapAdminWatermarkTemplatesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/watermark-templates");

        group.MapPost("/", CreateWatermarkTemplateAsync);
        group.MapGet("/", ListWatermarkTemplatesAsync);
        group.MapGet("/{watermarkTemplateId:guid}", GetWatermarkTemplateAsync);
        group.MapPut("/{watermarkTemplateId:guid}", UpdateWatermarkTemplateAsync);

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
            OpacityPercent = request.OpacityPercent ?? 33,
            DensityTiles = request.DensityTiles ?? 4,
            DiagonalAngleDegrees = request.DiagonalAngleDegrees ?? -28,
            IncludeUserId = request.IncludeUserId ?? true,
            IncludeTimestamp = request.IncludeTimestamp ?? true,
            IncludeIpAddress = request.IncludeIpAddress ?? false,
            IncludeSessionId = request.IncludeSessionId ?? false,
            RollingEnabled = request.RollingEnabled ?? false,
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

    private static async Task<Results<Ok<WatermarkTemplateResponse>, NotFound, BadRequest<ErrorResponse>>> UpdateWatermarkTemplateAsync(
        Guid watermarkTemplateId,
        UpdateWatermarkTemplateRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateUpdateRequest(request);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        var template = await dbContext.WatermarkTemplates
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId
                    && candidate.WatermarkTemplateId == watermarkTemplateId,
                cancellationToken);

        if (template is null)
        {
            return TypedResults.NotFound();
        }

        template.Name = request.Name.Trim();
        template.Pattern = request.Pattern.Trim();
        template.OpacityPercent = request.OpacityPercent;
        template.DensityTiles = request.DensityTiles;
        template.DiagonalAngleDegrees = request.DiagonalAngleDegrees;
        template.IncludeUserId = request.IncludeUserId;
        template.IncludeTimestamp = request.IncludeTimestamp;
        template.IncludeIpAddress = request.IncludeIpAddress;
        template.IncludeSessionId = request.IncludeSessionId;
        template.RollingEnabled = request.RollingEnabled;

        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "watermark_template_updated"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(WatermarkTemplateResponse.From(template));
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

        return ValidateAntiCaptureRanges(
            request.OpacityPercent,
            request.DensityTiles,
            request.DiagonalAngleDegrees);
    }

    private static ErrorResponse? ValidateUpdateRequest(UpdateWatermarkTemplateRequest request)
    {
        if (request.TenantId == Guid.Empty)
        {
            return new ErrorResponse("invalid_tenant_id");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
        {
            return new ErrorResponse("invalid_name");
        }

        if (string.IsNullOrWhiteSpace(request.Pattern) || request.Pattern.Length > MaxPatternLength)
        {
            return new ErrorResponse("invalid_pattern");
        }

        return ValidateAntiCaptureRanges(
            request.OpacityPercent,
            request.DensityTiles,
            request.DiagonalAngleDegrees);
    }

    private static ErrorResponse? ValidateAntiCaptureRanges(int? opacity, int? density, int? angle)
    {
        if (opacity is { } o && (o < MinOpacityPercent || o > MaxOpacityPercent))
        {
            return new ErrorResponse("invalid_opacity_percent");
        }

        if (density is { } d && (d < MinDensityTiles || d > MaxDensityTiles))
        {
            return new ErrorResponse("invalid_density_tiles");
        }

        if (angle is { } a && (a < MinAngleDegrees || a > MaxAngleDegrees))
        {
            return new ErrorResponse("invalid_diagonal_angle_degrees");
        }

        return null;
    }

    private sealed record CreateWatermarkTemplateRequest(
        Guid TenantId,
        Guid WatermarkTemplateId,
        string Name,
        string Pattern,
        int? OpacityPercent = null,
        int? DensityTiles = null,
        int? DiagonalAngleDegrees = null,
        bool? IncludeUserId = null,
        bool? IncludeTimestamp = null,
        bool? IncludeIpAddress = null,
        bool? IncludeSessionId = null,
        bool? RollingEnabled = null);

    private sealed record UpdateWatermarkTemplateRequest(
        Guid TenantId,
        string Name,
        string Pattern,
        int OpacityPercent,
        int DensityTiles,
        int DiagonalAngleDegrees,
        bool IncludeUserId,
        bool IncludeTimestamp,
        bool IncludeIpAddress,
        bool IncludeSessionId,
        bool RollingEnabled);

    private sealed record WatermarkTemplateResponse(
        Guid TenantId,
        Guid WatermarkTemplateId,
        string Name,
        string Pattern,
        int OpacityPercent,
        int DensityTiles,
        int DiagonalAngleDegrees,
        bool IncludeUserId,
        bool IncludeTimestamp,
        bool IncludeIpAddress,
        bool IncludeSessionId,
        bool RollingEnabled,
        DateTimeOffset CreatedAtUtc)
    {
        public static WatermarkTemplateResponse From(WatermarkTemplateEntity template)
            => new(
                template.TenantId,
                template.WatermarkTemplateId,
                template.Name,
                template.Pattern,
                template.OpacityPercent,
                template.DensityTiles,
                template.DiagonalAngleDegrees,
                template.IncludeUserId,
                template.IncludeTimestamp,
                template.IncludeIpAddress,
                template.IncludeSessionId,
                template.RollingEnabled,
                template.CreatedAtUtc);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
