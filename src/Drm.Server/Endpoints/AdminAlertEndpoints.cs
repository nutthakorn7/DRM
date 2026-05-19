using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminAlertEndpoints
{
    public static IEndpointRouteBuilder MapAdminAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var rules = endpoints.MapGroup("/api/admin/alert-rules");
        rules.MapGet("/", ListRulesAsync);
        rules.MapPost("/", CreateRuleAsync);
        rules.MapPatch("/{ruleId:guid}", UpdateRuleAsync);
        rules.MapDelete("/{ruleId:guid}", DeleteRuleAsync);

        endpoints.MapGet("/api/admin/alerts-fired", ListFiredAsync);

        return endpoints;
    }

    private static async Task<IResult> ListRulesAsync(
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var ruleEntities = await dbContext.OperatorAlertRules
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Results.Ok(ruleEntities
            .OrderBy(r => r.CreatedAtUtc)
            .Select(AlertRuleResponse.From));
    }

    private static async Task<IResult> CreateRuleAsync(
        CreateAlertRuleRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { reasonCode = "name_required" });

        if (!Enum.IsDefined(typeof(AlertCondition), request.Condition))
            return Results.BadRequest(new { reasonCode = "invalid_condition" });

        if (request.Threshold < 0)
            return Results.BadRequest(new { reasonCode = "invalid_threshold" });

        // SeatUsagePct and TenantInactiveDays require a tenantId
        if (request.Condition is (int)AlertCondition.SeatUsagePctAbove or (int)AlertCondition.TenantInactiveDays
            && request.TenantId is null)
            return Results.BadRequest(new { reasonCode = "tenant_id_required_for_condition" });

        var entity = new OperatorAlertRuleEntity
        {
            RuleId = Guid.NewGuid(),
            Name = request.Name.Trim(),
            TenantId = request.TenantId,
            Condition = (AlertCondition)request.Condition,
            Threshold = request.Threshold,
            Enabled = request.Enabled ?? true,
            FireWebhook = request.FireWebhook ?? false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.OperatorAlertRules.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/admin/alert-rules/{entity.RuleId}", AlertRuleResponse.From(entity));
    }

    private static async Task<IResult> UpdateRuleAsync(
        Guid ruleId,
        UpdateAlertRuleRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var rule = await dbContext.OperatorAlertRules.FirstOrDefaultAsync(r => r.RuleId == ruleId, cancellationToken);
        if (rule is null) return Results.NotFound();

        if (request.Name is not null) rule.Name = request.Name.Trim();
        if (request.Threshold is not null) rule.Threshold = request.Threshold.Value;
        if (request.Enabled is not null) rule.Enabled = request.Enabled.Value;
        if (request.FireWebhook is not null) rule.FireWebhook = request.FireWebhook.Value;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(AlertRuleResponse.From(rule));
    }

    private static async Task<IResult> DeleteRuleAsync(
        Guid ruleId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var rule = await dbContext.OperatorAlertRules.FirstOrDefaultAsync(r => r.RuleId == ruleId, cancellationToken);
        if (rule is null) return Results.NotFound();

        dbContext.OperatorAlertRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListFiredAsync(
        HttpContext httpContext,
        AppDbContext dbContext,
        int? limit,
        Guid? ruleId,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var cap = Math.Min(limit ?? 100, 500);

        var query = dbContext.OperatorAlertsFired.AsNoTracking();
        if (ruleId.HasValue)
            query = query.Where(f => f.RuleId == ruleId.Value);

        var rows = await query.ToListAsync(cancellationToken);
        var result = rows
            .OrderByDescending(f => f.FiredAtUtc)
            .Take(cap)
            .Select(AlertFiredResponse.From)
            .ToList();

        return Results.Ok(result);
    }

    private sealed record CreateAlertRuleRequest(
        string? Name,
        Guid? TenantId,
        int Condition,
        double Threshold,
        bool? Enabled,
        bool? FireWebhook);

    private sealed record UpdateAlertRuleRequest(
        string? Name,
        double? Threshold,
        bool? Enabled,
        bool? FireWebhook);

    private sealed record AlertRuleResponse(
        Guid RuleId,
        string Name,
        Guid? TenantId,
        int Condition,
        string ConditionName,
        double Threshold,
        bool Enabled,
        bool FireWebhook,
        DateTimeOffset CreatedAtUtc)
    {
        public static AlertRuleResponse From(OperatorAlertRuleEntity r) => new(
            r.RuleId, r.Name, r.TenantId,
            (int)r.Condition, r.Condition.ToString(),
            r.Threshold, r.Enabled, r.FireWebhook, r.CreatedAtUtc);
    }

    private sealed record AlertFiredResponse(
        long Id,
        Guid RuleId,
        Guid? TenantId,
        string RuleName,
        double ActualValue,
        double Threshold,
        DateTimeOffset FiredAtUtc)
    {
        public static AlertFiredResponse From(OperatorAlertFiredEntity f) => new(
            f.Id, f.RuleId, f.TenantId, f.RuleName, f.ActualValue, f.Threshold, f.FiredAtUtc);
    }
}
