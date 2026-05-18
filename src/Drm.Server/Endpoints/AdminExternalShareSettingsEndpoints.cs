using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminExternalShareSettingsEndpoints
{
    private static readonly HashSet<string> SupportedHistoryReasonCodes = new(StringComparer.Ordinal)
    {
        "external_sharing_enabled",
        "external_sharing_disabled",
        "external_share_link_created",
        "external_share_link_revoked",
        "external_share_link_revoked_by_guest",
        "external_share_link_revoked_by_domain",
        "external_share_link_extended",
        "external_share_link_token_rotated",
        "external_share_guest_domain_allowlist_updated",
        "external_share_guest_blocklist_updated",
        "external_share_policy_limits_updated",
        "external_share_tenant_lockdown",
        "external_share_link_revoked_by_lockdown"
    };
    private static readonly HashSet<string> SupportedHistoryScopes = new(StringComparer.Ordinal)
    {
        "tenant_wide",
        "file_scoped"
    };

    public static IEndpointRouteBuilder MapAdminExternalShareSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/external-share/settings");

        group.MapGet("/", GetSettingsAsync);
        group.MapGet("/history", GetSettingsHistoryAsync);
        group.MapGet("/history/page", GetSettingsHistoryPageAsync);
        group.MapGet("/history.csv", GetSettingsHistoryCsvAsync);
        group.MapGet("/history/summary", GetSettingsHistorySummaryAsync);
        group.MapGet("/history/summary.csv", GetSettingsHistorySummaryCsvAsync);
        group.MapGet("/history/trend", GetSettingsHistoryTrendAsync);
        group.MapGet("/history/trend.csv", GetSettingsHistoryTrendCsvAsync);
        group.MapPost("/", UpsertSettingsAsync);
        group.MapPost("/lockdown", LockdownTenantExternalSharingAsync);

        return endpoints;
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        var settings = await dbContext.TenantExternalShareSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId, cancellationToken);
        if (settings is null)
        {
            return Results.Ok(ExternalShareSettingsResponse.Default(tenantId));
        }

        return Results.Ok(ExternalShareSettingsResponse.From(settings));
    }

    private static async Task<IResult> GetSettingsHistoryAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? limit,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var cappedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var scanLimit = parsedFromUtc.HasValue || parsedToUtc.HasValue
            ? 50_000
            : cappedLimit;
        var events = await FilterSettingsHistoryEvents(
                dbContext,
                tenantId,
                normalizedReasonCode,
                parsedUserId,
                parsedFileId,
                normalizedScope)
            .OrderByDescending(candidate => candidate.Id)
            .Take(scanLimit)
            .Select(candidate => new ExternalShareSettingsHistoryEventResponse(
                candidate.Id,
                candidate.TenantId,
                candidate.FileId,
                candidate.UserId,
                candidate.ReasonCode,
                candidate.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var orderedEvents = ApplyHistoryTimeRange(events, parsedFromUtc, parsedToUtc)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Take(cappedLimit)
            .ToList();
        var enrichedEvents = await EnrichHistoryEventsAsync(
            orderedEvents,
            tenantId,
            dbContext,
            cancellationToken);
        return Results.Ok<IReadOnlyList<ExternalShareSettingsHistoryEventResponse>>(enrichedEvents);
    }

    private static async Task<IResult> GetSettingsHistoryPageAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? limit,
        long? beforeId,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (beforeId.HasValue && beforeId.Value <= 0)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_before_id_filter"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var cappedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var scanLimit = parsedFromUtc.HasValue || parsedToUtc.HasValue
            ? 50_000
            : cappedLimit;
        var query = FilterSettingsHistoryEvents(
            dbContext,
            tenantId,
            normalizedReasonCode,
            parsedUserId,
            parsedFileId,
            normalizedScope);
        if (beforeId.HasValue)
        {
            query = query.Where(candidate => candidate.Id < beforeId.Value);
        }

        var events = await query
            .OrderByDescending(candidate => candidate.Id)
            .Take(scanLimit)
            .Select(candidate => new ExternalShareSettingsHistoryEventResponse(
                candidate.Id,
                candidate.TenantId,
                candidate.FileId,
                candidate.UserId,
                candidate.ReasonCode,
                candidate.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var orderedEvents = ApplyHistoryTimeRange(events, parsedFromUtc, parsedToUtc)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Take(cappedLimit)
            .ToList();

        long? nextCursorId = null;
        var hasMore = false;
        if (orderedEvents.Count == cappedLimit)
        {
            nextCursorId = orderedEvents[^1].Id;
            hasMore = true;
        }

        var enrichedEvents = await EnrichHistoryEventsAsync(
            orderedEvents,
            tenantId,
            dbContext,
            cancellationToken);

        return Results.Ok(new ExternalShareSettingsHistoryPageResponse(
            enrichedEvents,
            nextCursorId,
            hasMore));
    }

    private static async Task<IResult> GetSettingsHistoryCsvAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? limit,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var cappedLimit = Math.Clamp(limit ?? 500, 1, 5000);
        var scanLimit = parsedFromUtc.HasValue || parsedToUtc.HasValue
            ? 100_000
            : cappedLimit;
        var events = await FilterSettingsHistoryEvents(
                dbContext,
                tenantId,
                normalizedReasonCode,
                parsedUserId,
                parsedFileId,
                normalizedScope)
            .OrderByDescending(candidate => candidate.Id)
            .Take(scanLimit)
            .Select(candidate => new ExternalShareSettingsHistoryEventResponse(
                candidate.Id,
                candidate.TenantId,
                candidate.FileId,
                candidate.UserId,
                candidate.ReasonCode,
                candidate.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var orderedEvents = ApplyHistoryTimeRange(events, parsedFromUtc, parsedToUtc)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Take(cappedLimit)
            .ToList();
        var enrichedEvents = await EnrichHistoryEventsAsync(
            orderedEvents,
            tenantId,
            dbContext,
            cancellationToken);
        var csv = BuildSettingsHistoryCsv(enrichedEvents);
        return Results.Text(csv, "text/csv");
    }

    private static async Task<IResult> GetSettingsHistorySummaryAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? windowHours,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var effectiveWindowHours = windowHours ?? 168;
        if (effectiveWindowHours < 1 || effectiveWindowHours > 720)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_window_hours"));
        }

        var summary = await BuildSettingsHistorySummaryAsync(
            tenantId,
            effectiveWindowHours,
            normalizedReasonCode,
            parsedUserId,
            parsedFileId,
            normalizedScope,
            parsedFromUtc,
            parsedToUtc,
            dbContext,
            cancellationToken);
        return Results.Ok(summary);
    }

    private static async Task<IResult> GetSettingsHistorySummaryCsvAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? windowHours,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var effectiveWindowHours = windowHours ?? 168;
        if (effectiveWindowHours < 1 || effectiveWindowHours > 720)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_window_hours"));
        }

        var summary = await BuildSettingsHistorySummaryAsync(
            tenantId,
            effectiveWindowHours,
            normalizedReasonCode,
            parsedUserId,
            parsedFileId,
            normalizedScope,
            parsedFromUtc,
            parsedToUtc,
            dbContext,
            cancellationToken);
        var csv = BuildSettingsHistorySummaryCsv(summary);
        return Results.Text(csv, "text/csv");
    }

    private static async Task<IResult> GetSettingsHistoryTrendAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? windowDays,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var effectiveWindowDays = windowDays ?? 14;
        if (effectiveWindowDays < 1 || effectiveWindowDays > 365)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_window_days"));
        }

        var sinceUtc = DateTimeOffset.UtcNow.Date.AddDays(-(effectiveWindowDays - 1));
        var filteredEvents = await FilterSettingsHistoryEvents(
                dbContext,
                tenantId,
                normalizedReasonCode,
                parsedUserId,
                parsedFileId,
                normalizedScope)
            .OrderByDescending(candidate => candidate.Id)
            .Take(20_000)
            .Select(candidate => new ExternalShareSettingsHistoryEventResponse(
                candidate.Id,
                candidate.TenantId,
                candidate.FileId,
                candidate.UserId,
                candidate.ReasonCode,
                candidate.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var effectiveFromUtc = parsedFromUtc.HasValue && parsedFromUtc.Value > sinceUtc
            ? parsedFromUtc.Value
            : sinceUtc;
        var points = BuildSettingsHistoryTrendPoints(filteredEvents, effectiveFromUtc, parsedToUtc);
        return Results.Ok<IReadOnlyList<ExternalShareSettingsHistoryTrendPointResponse>>(points);
    }

    private static async Task<IResult> GetSettingsHistoryTrendCsvAsync(
        Guid tenantId,
        HttpContext httpContext,
        int? windowDays,
        string? reasonCode,
        string? userId,
        string? userEmail,
        string? fileId,
        string? scope,
        string? fromUtc,
        string? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (!TryNormalizeHistoryReasonCode(reasonCode, out var normalizedReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_reason_filter"));
        }

        if (!TryParseOptionalGuid(userId, out var parsedUserId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_id_filter"));
        }

        if (!TryNormalizeHistoryUserEmail(userEmail, out var normalizedUserEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_user_email_filter"));
        }

        if (!TryParseOptionalGuid(fileId, out var parsedFileId))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_file_id_filter"));
        }

        if (!TryNormalizeHistoryScope(scope, out var normalizedScope))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_scope_filter"));
        }

        parsedUserId = await ResolveHistoryUserIdFilterAsync(
            tenantId,
            parsedUserId,
            normalizedUserEmail,
            dbContext,
            cancellationToken);

        if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_from_utc_filter"));
        }

        if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_to_utc_filter"));
        }

        if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_time_range"));
        }

        var effectiveWindowDays = windowDays ?? 14;
        if (effectiveWindowDays < 1 || effectiveWindowDays > 365)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_external_share_history_window_days"));
        }

        var sinceUtc = DateTimeOffset.UtcNow.Date.AddDays(-(effectiveWindowDays - 1));
        var filteredEvents = await FilterSettingsHistoryEvents(
                dbContext,
                tenantId,
                normalizedReasonCode,
                parsedUserId,
                parsedFileId,
                normalizedScope)
            .OrderByDescending(candidate => candidate.Id)
            .Take(20_000)
            .Select(candidate => new ExternalShareSettingsHistoryEventResponse(
                candidate.Id,
                candidate.TenantId,
                candidate.FileId,
                candidate.UserId,
                candidate.ReasonCode,
                candidate.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var effectiveFromUtc = parsedFromUtc.HasValue && parsedFromUtc.Value > sinceUtc
            ? parsedFromUtc.Value
            : sinceUtc;
        var points = BuildSettingsHistoryTrendPoints(filteredEvents, effectiveFromUtc, parsedToUtc);
        var csv = BuildSettingsHistoryTrendCsv(points);
        return Results.Text(csv, "text/csv");
    }

    private static IQueryable<AuditEventEntity> FilterSettingsHistoryEvents(
        AppDbContext dbContext,
        Guid tenantId,
        string? reasonCode,
        Guid? userId,
        Guid? fileId,
        string? scope)
    {
        var query = dbContext.AuditEvents
            .AsNoTracking()
            .Where(candidate =>
                candidate.TenantId == tenantId &&
                candidate.EventType == "external_share_changed");

        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            query = query.Where(candidate => candidate.ReasonCode == reasonCode);
        }

        if (userId.HasValue)
        {
            query = query.Where(candidate => candidate.UserId == userId);
        }

        if (fileId.HasValue)
        {
            query = query.Where(candidate => candidate.FileId == fileId);
        }

        if (string.Equals(scope, "tenant_wide", StringComparison.Ordinal))
        {
            query = query.Where(candidate => candidate.FileId == null);
        }
        else if (string.Equals(scope, "file_scoped", StringComparison.Ordinal))
        {
            query = query.Where(candidate => candidate.FileId != null);
        }

        return query;
    }

    private static async Task<ExternalShareSettingsHistorySummaryResponse> BuildSettingsHistorySummaryAsync(
        Guid tenantId,
        int effectiveWindowHours,
        string? reasonCode,
        Guid? userId,
        Guid? fileId,
        string? scope,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sinceUtc = DateTimeOffset.UtcNow.AddHours(-effectiveWindowHours);
        var filteredEvents = await FilterSettingsHistoryEvents(
                dbContext,
                tenantId,
                reasonCode,
                userId,
                fileId,
                scope)
            .OrderByDescending(candidate => candidate.Id)
            .Take(10_000)
            .Select(candidate => new ExternalShareSettingsHistoryEventResponse(
                candidate.Id,
                candidate.TenantId,
                candidate.FileId,
                candidate.UserId,
                candidate.ReasonCode,
                candidate.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var scopedEvents = ApplyHistoryTimeRange(
                filteredEvents.Where(candidate => candidate.CreatedAtUtc >= sinceUtc),
                fromUtc,
                toUtc)
            .ToList();
        var latestEvent = scopedEvents
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefault();

        var totalEvents = scopedEvents.Count;
        var tenantWideEvents = scopedEvents.Count(candidate => candidate.FileId == null);
        var fileScopedEvents = scopedEvents.Count(candidate => candidate.FileId != null);
        var uniqueUsers = scopedEvents
            .Where(candidate => candidate.UserId != null)
            .Select(candidate => candidate.UserId)
            .Distinct()
            .Count();

        var reasonCounts = scopedEvents
            .GroupBy(candidate => candidate.ReasonCode)
            .Select(group => new ExternalShareSettingsHistoryReasonCountResponse(
                group.Key,
                group.Count(),
                group.Count(candidate => candidate.FileId == null),
                group.Count(candidate => candidate.FileId != null)))
            .OrderByDescending(candidate => candidate.Count)
            .ThenBy(candidate => candidate.ReasonCode)
            .ToList();

        var topUserCounts = scopedEvents
            .Where(candidate => candidate.UserId != null)
            .GroupBy(candidate => candidate.UserId!.Value)
            .Select(group => new
            {
                UserId = group.Key,
                Count = group.Count(),
                TenantWideCount = group.Count(candidate => candidate.FileId == null),
                FileScopedCount = group.Count(candidate => candidate.FileId != null)
            })
            .OrderByDescending(candidate => candidate.Count)
            .ThenBy(candidate => candidate.UserId)
            .Take(10)
            .ToList();

        var profileUserIds = topUserCounts
            .Select(candidate => candidate.UserId)
            .Distinct()
            .ToList();
        if (latestEvent?.UserId is Guid latestEventUserId &&
            !profileUserIds.Contains(latestEventUserId))
        {
            profileUserIds.Add(latestEventUserId);
        }

        var topUserIds = profileUserIds.ToArray();
        var userProfiles = topUserIds.Length == 0
            ? new Dictionary<Guid, TenantUserEntity>()
            : await dbContext.TenantUsers
                .AsNoTracking()
                .Where(candidate =>
                    candidate.TenantId == tenantId &&
                    topUserIds.Contains(candidate.UserId))
                .ToDictionaryAsync(candidate => candidate.UserId, cancellationToken);
        var topUsers = topUserCounts
            .Select(candidate =>
            {
                userProfiles.TryGetValue(candidate.UserId, out var profile);
                var email = string.IsNullOrWhiteSpace(profile?.Email)
                    ? null
                    : profile.Email;
                var displayName = string.IsNullOrWhiteSpace(profile?.DisplayName)
                    ? null
                    : profile.DisplayName;
                return new ExternalShareSettingsHistoryUserCountResponse(
                    candidate.UserId,
                    email,
                    displayName,
                    candidate.Count,
                    candidate.TenantWideCount,
                    candidate.FileScopedCount);
            })
            .ToList();
        userProfiles.TryGetValue(latestEvent?.UserId ?? Guid.Empty, out var latestEventUserProfile);
        var latestEventUserEmail = string.IsNullOrWhiteSpace(latestEventUserProfile?.Email)
            ? null
            : latestEventUserProfile.Email;
        var latestEventUserDisplayName = string.IsNullOrWhiteSpace(latestEventUserProfile?.DisplayName)
            ? null
            : latestEventUserProfile.DisplayName;

        var topFileCounts = scopedEvents
            .Where(candidate => candidate.FileId != null)
            .GroupBy(candidate => candidate.FileId!.Value)
            .Select(group => new
            {
                FileId = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(candidate => candidate.Count)
            .ThenBy(candidate => candidate.FileId)
            .Take(10)
            .ToList();
        var topFileIds = topFileCounts
            .Select(candidate => candidate.FileId)
            .Distinct()
            .ToList();
        if (latestEvent?.FileId is Guid latestEventFileId &&
            !topFileIds.Contains(latestEventFileId))
        {
            topFileIds.Add(latestEventFileId);
        }

        var topFileMetadataIds = topFileIds.ToArray();
        var topFileMetadata = topFileMetadataIds.Length == 0
            ? new Dictionary<Guid, ProtectedFileEntity>()
            : await dbContext.ProtectedFiles
                .AsNoTracking()
                .Where(candidate =>
                    candidate.TenantId == tenantId &&
                    topFileMetadataIds.Contains(candidate.Id))
                .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        var topFiles = topFileCounts
            .Select(candidate =>
            {
                topFileMetadata.TryGetValue(candidate.FileId, out var file);
                var contentType = string.IsNullOrWhiteSpace(file?.ContentType)
                    ? null
                    : file.ContentType;
                var revoked = file?.Revoked;
                return new ExternalShareSettingsHistoryFileCountResponse(
                    candidate.FileId,
                    contentType,
                    revoked,
                    candidate.Count);
            })
            .ToList();
        topFileMetadata.TryGetValue(latestEvent?.FileId ?? Guid.Empty, out var latestEventFile);
        var latestEventFileContentType = string.IsNullOrWhiteSpace(latestEventFile?.ContentType)
            ? null
            : latestEventFile.ContentType;
        var latestEventFileRevoked = latestEventFile?.Revoked;

        return new ExternalShareSettingsHistorySummaryResponse(
            tenantId,
            effectiveWindowHours,
            totalEvents,
            uniqueUsers,
            tenantWideEvents,
            fileScopedEvents,
            reasonCounts,
            topUsers,
            topFiles,
            latestEvent?.CreatedAtUtc,
            latestEvent?.ReasonCode,
            latestEvent?.FileId,
            latestEventFileContentType,
            latestEventFileRevoked,
            latestEvent?.UserId,
            latestEventUserEmail,
            latestEventUserDisplayName);
    }

    private static bool TryNormalizeHistoryReasonCode(string? reasonCode, out string? normalizedReasonCode)
    {
        normalizedReasonCode = null;
        var raw = reasonCode?.Trim();
        if (string.IsNullOrEmpty(raw) || string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!SupportedHistoryReasonCodes.Contains(raw))
        {
            return false;
        }

        normalizedReasonCode = raw;
        return true;
    }

    private static bool TryNormalizeHistoryScope(string? scope, out string? normalizedScope)
    {
        normalizedScope = null;
        var raw = scope?.Trim();
        if (string.IsNullOrEmpty(raw) || string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!SupportedHistoryScopes.Contains(raw))
        {
            return false;
        }

        normalizedScope = raw;
        return true;
    }

    private static bool TryNormalizeHistoryUserEmail(string? userEmail, out string? normalizedUserEmail)
    {
        normalizedUserEmail = null;
        var raw = userEmail?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        if (raw.Contains(' ', StringComparison.Ordinal) ||
            !raw.Contains('@', StringComparison.Ordinal) ||
            raw.StartsWith('@') ||
            raw.EndsWith('@'))
        {
            return false;
        }

        normalizedUserEmail = raw.ToLowerInvariant();
        return true;
    }

    private static async Task<Guid?> ResolveHistoryUserIdFilterAsync(
        Guid tenantId,
        Guid? parsedUserId,
        string? normalizedUserEmail,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(normalizedUserEmail))
        {
            return parsedUserId;
        }

        var matchedUserId = await dbContext.TenantUsers
            .AsNoTracking()
            .Where(candidate =>
                candidate.TenantId == tenantId &&
                candidate.Email != null &&
                candidate.Email.ToLower() == normalizedUserEmail)
            .Select(candidate => (Guid?)candidate.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!matchedUserId.HasValue)
        {
            return Guid.Empty;
        }

        if (parsedUserId.HasValue && parsedUserId.Value != matchedUserId.Value)
        {
            return Guid.Empty;
        }

        return matchedUserId.Value;
    }

    private static bool TryParseOptionalGuid(string? rawValue, out Guid? parsed)
    {
        parsed = null;
        var raw = rawValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        if (!Guid.TryParse(raw, out var guid) || guid == Guid.Empty)
        {
            return false;
        }

        parsed = guid;
        return true;
    }

    private static bool TryParseOptionalDateTimeOffset(string? rawValue, out DateTimeOffset? parsed)
    {
        parsed = null;
        var raw = rawValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(raw, out var value))
        {
            return false;
        }

        parsed = value;
        return true;
    }

    private static IEnumerable<ExternalShareSettingsHistoryEventResponse> ApplyHistoryTimeRange(
        IEnumerable<ExternalShareSettingsHistoryEventResponse> events,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        var filtered = events;
        if (fromUtc.HasValue)
        {
            filtered = filtered.Where(candidate => candidate.CreatedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            filtered = filtered.Where(candidate => candidate.CreatedAtUtc <= toUtc.Value);
        }

        return filtered;
    }

    private static async Task<List<ExternalShareSettingsHistoryEventResponse>> EnrichHistoryEventsAsync(
        IReadOnlyList<ExternalShareSettingsHistoryEventResponse> events,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return [];
        }

        var userIds = events
            .Where(candidate => candidate.UserId != null)
            .Select(candidate => candidate.UserId!.Value)
            .Distinct()
            .ToArray();

        var fileIds = events
            .Where(candidate => candidate.FileId != null)
            .Select(candidate => candidate.FileId!.Value)
            .Distinct()
            .ToArray();
        if (userIds.Length == 0 && fileIds.Length == 0)
        {
            return [.. events];
        }

        Dictionary<Guid, TenantUserEntity> userProfiles;
        if (userIds.Length == 0)
        {
            userProfiles = [];
        }
        else
        {
            userProfiles = await dbContext.TenantUsers
                .AsNoTracking()
                .Where(candidate =>
                    candidate.TenantId == tenantId &&
                    userIds.Contains(candidate.UserId))
                .ToDictionaryAsync(candidate => candidate.UserId, cancellationToken);
        }

        Dictionary<Guid, ProtectedFileEntity> fileMetadata;
        if (fileIds.Length == 0)
        {
            fileMetadata = [];
        }
        else
        {
            fileMetadata = await dbContext.ProtectedFiles
                .AsNoTracking()
                .Where(candidate =>
                    candidate.TenantId == tenantId &&
                    fileIds.Contains(candidate.Id))
                .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        }

        return events
            .Select(candidate =>
            {
                string? email = null;
                string? displayName = null;
                if (candidate.UserId is Guid userId &&
                    userProfiles.TryGetValue(userId, out var profile))
                {
                    email = string.IsNullOrWhiteSpace(profile.Email)
                        ? null
                        : profile.Email;
                    displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? null
                        : profile.DisplayName;
                }

                string? fileContentType = null;
                bool? fileRevoked = null;
                if (candidate.FileId is Guid fileId &&
                    fileMetadata.TryGetValue(fileId, out var file))
                {
                    fileContentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? null
                        : file.ContentType;
                    fileRevoked = file.Revoked;
                }

                return candidate with
                {
                    UserEmail = email,
                    UserDisplayName = displayName,
                    FileContentType = fileContentType,
                    FileRevoked = fileRevoked
                };
            })
            .ToList();
    }

    private static string BuildSettingsHistoryCsv(IReadOnlyList<ExternalShareSettingsHistoryEventResponse> events)
    {
        var csv = new StringBuilder();
        csv.Append("id,createdAtUtc,tenantId,fileId,fileContentType,fileRevoked,userId,userEmail,userDisplayName,reasonCode\r\n");

        foreach (var entry in events)
        {
            csv.Append(EscapeCsv(entry.Id.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.CreatedAtUtc.ToString("O")));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.TenantId.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.FileId?.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.FileContentType ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(FormatNullableBoolean(entry.FileRevoked)));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.UserId?.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.UserEmail ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.UserDisplayName ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(entry.ReasonCode));
            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    private static string BuildSettingsHistorySummaryCsv(ExternalShareSettingsHistorySummaryResponse summary)
    {
        var csv = new StringBuilder();
        csv.Append("tenantId,windowHours,totalEvents,uniqueUsers,tenantWideEvents,fileScopedEvents,reasonCode,reasonCount,reasonTenantWideCount,reasonFileScopedCount,topUserId,topUserEmail,topUserDisplayName,topUserCount,topUserTenantWideCount,topUserFileScopedCount,topFileId,topFileContentType,topFileRevoked,topFileCount,latestEventAtUtc,latestEventReasonCode,latestEventFileId,latestEventFileContentType,latestEventFileRevoked,latestEventUserId,latestEventUserEmail,latestEventUserDisplayName\r\n");

        var rowCount = Math.Max(
            1,
            Math.Max(
                summary.ReasonCounts.Count,
                Math.Max(summary.TopUsers.Count, summary.TopFiles.Count)));

        for (var index = 0; index < rowCount; index += 1)
        {
            var reasonCount = index < summary.ReasonCounts.Count
                ? summary.ReasonCounts[index]
                : null;
            var topUser = index < summary.TopUsers.Count
                ? summary.TopUsers[index]
                : null;
            var topFile = index < summary.TopFiles.Count
                ? summary.TopFiles[index]
                : null;

            csv.Append(EscapeCsv(summary.TenantId.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.WindowHours.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.TotalEvents.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.UniqueUsers.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.TenantWideEvents.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.FileScopedEvents.ToString()));
            csv.Append(',');
            csv.Append(EscapeCsv(reasonCount?.ReasonCode ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(reasonCount?.Count.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(reasonCount?.TenantWideCount.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(reasonCount?.FileScopedCount.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topUser?.UserId.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topUser?.Email ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topUser?.DisplayName ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topUser?.Count.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topUser?.TenantWideCount.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topUser?.FileScopedCount.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topFile?.FileId.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(topFile?.ContentType ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(FormatNullableBoolean(topFile?.Revoked)));
            csv.Append(',');
            csv.Append(EscapeCsv(topFile?.Count.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventAtUtc?.ToString("O") ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventReasonCode ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventFileId?.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventFileContentType ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(FormatNullableBoolean(summary.LatestEventFileRevoked)));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventUserId?.ToString() ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventUserEmail ?? string.Empty));
            csv.Append(',');
            csv.Append(EscapeCsv(summary.LatestEventUserDisplayName ?? string.Empty));
            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    private static string FormatNullableBoolean(bool? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        return value.Value ? "true" : "false";
    }

    private static List<ExternalShareSettingsHistoryTrendPointResponse> BuildSettingsHistoryTrendPoints(
        IReadOnlyList<ExternalShareSettingsHistoryEventResponse> events,
        DateTimeOffset fromUtc,
        DateTimeOffset? toUtc)
    {
        return ApplyHistoryTimeRange(events, fromUtc, toUtc)
            .GroupBy(candidate => DateOnly.FromDateTime(candidate.CreatedAtUtc.UtcDateTime.Date))
            .Select(group =>
            {
                var reasonCounts = group
                    .GroupBy(candidate => candidate.ReasonCode)
                    .Select(reasonGroup => new ExternalShareSettingsHistoryReasonCountResponse(
                        reasonGroup.Key,
                        reasonGroup.Count(),
                        reasonGroup.Count(candidate => candidate.FileId == null),
                        reasonGroup.Count(candidate => candidate.FileId != null)))
                    .OrderByDescending(candidate => candidate.Count)
                    .ThenBy(candidate => candidate.ReasonCode)
                    .ToList();

                return new ExternalShareSettingsHistoryTrendPointResponse(
                    group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    group.Count(),
                    group.Where(candidate => candidate.UserId != null)
                        .Select(candidate => candidate.UserId)
                        .Distinct()
                        .Count(),
                    group.Count(candidate => candidate.FileId == null),
                    group.Count(candidate => candidate.FileId != null),
                    reasonCounts);
            })
            .OrderByDescending(candidate => candidate.DayUtc)
            .ToList();
    }

    private static string BuildSettingsHistoryTrendCsv(IReadOnlyList<ExternalShareSettingsHistoryTrendPointResponse> points)
    {
        var csv = new StringBuilder();
        csv.Append("dayUtc,totalEvents,uniqueUsers,tenantWideEvents,fileScopedEvents,reasonCode,reasonCount,reasonTenantWideCount,reasonFileScopedCount\r\n");

        foreach (var point in points)
        {
            if (point.ReasonCounts.Count == 0)
            {
                csv.Append(EscapeCsv(point.DayUtc));
                csv.Append(',');
                csv.Append(EscapeCsv(point.TotalEvents.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(point.UniqueUsers.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(point.TenantWideEvents.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(point.FileScopedEvents.ToString()));
                csv.Append(',');
                csv.Append(',');
                csv.Append(',');
                csv.Append(',');
                csv.Append("\r\n");
                continue;
            }

            foreach (var reasonCount in point.ReasonCounts)
            {
                csv.Append(EscapeCsv(point.DayUtc));
                csv.Append(',');
                csv.Append(EscapeCsv(point.TotalEvents.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(point.UniqueUsers.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(point.TenantWideEvents.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(point.FileScopedEvents.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(reasonCount.ReasonCode));
                csv.Append(',');
                csv.Append(EscapeCsv(reasonCount.Count.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(reasonCount.TenantWideCount.ToString()));
                csv.Append(',');
                csv.Append(EscapeCsv(reasonCount.FileScopedCount.ToString()));
                csv.Append("\r\n");
            }
        }

        return csv.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static async Task<IResult> UpsertSettingsAsync(
        UpsertExternalShareSettingsRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (request.AdminUserId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_admin_user_id"));
        }

        if (!ExternalShareSettingsGuard.TryNormalizeAllowedGuestEmailDomainsCsv(
                request.AllowedGuestEmailDomainsCsv,
                out var normalizedAllowedGuestEmailDomainsCsv))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_allowed_guest_email_domains"));
        }

        if (!ExternalShareSettingsGuard.TryNormalizeBlockedGuestEmailsCsv(
                request.BlockedGuestEmailsCsv,
                out var normalizedBlockedGuestEmailsCsv))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_blocked_guest_emails"));
        }

        if (!ExternalShareSettingsGuard.IsValidMaxShareLinkLifetimeHours(request.MaxShareLinkLifetimeHours))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_max_share_link_lifetime_hours"));
        }

        if (!ExternalShareSettingsGuard.IsValidMaxShareLinkMaxUses(request.MaxShareLinkMaxUses))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_max_share_link_max_uses"));
        }

        if (!ExternalShareSettingsGuard.IsValidMaxActiveShareLinksPerFile(request.MaxActiveShareLinksPerFile))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_max_active_share_links_per_file"));
        }

        var now = DateTimeOffset.UtcNow;
        var settings = await dbContext.TenantExternalShareSettings
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId, cancellationToken);
        var previousEnabled = settings?.ExternalSharingEnabled ?? true;
        var previousAllowedDomainsCsv = settings?.AllowedGuestEmailDomainsCsv ?? string.Empty;
        var previousBlockedGuestEmailsCsv = settings?.BlockedGuestEmailsCsv ?? string.Empty;
        var previousMaxShareLinkLifetimeHours = settings?.MaxShareLinkLifetimeHours;
        var previousMaxShareLinkMaxUses = settings?.MaxShareLinkMaxUses;
        var previousMaxActiveShareLinksPerFile = settings?.MaxActiveShareLinksPerFile;
        if (settings is null)
        {
            settings = new TenantExternalShareSettingsEntity
            {
                TenantId = request.TenantId
            };
            dbContext.TenantExternalShareSettings.Add(settings);
        }

        settings.ExternalSharingEnabled = request.ExternalSharingEnabled;
        settings.AllowedGuestEmailDomainsCsv = normalizedAllowedGuestEmailDomainsCsv;
        settings.BlockedGuestEmailsCsv = normalizedBlockedGuestEmailsCsv;
        settings.MaxShareLinkLifetimeHours = request.MaxShareLinkLifetimeHours;
        settings.MaxShareLinkMaxUses = request.MaxShareLinkMaxUses;
        settings.MaxActiveShareLinksPerFile = request.MaxActiveShareLinksPerFile;
        settings.UpdatedAtUtc = now;
        settings.UpdatedByUserId = request.AdminUserId;

        if (request.ExternalSharingEnabled != previousEnabled)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = null,
                UserId = request.AdminUserId,
                EventType = "external_share_changed",
                ReasonCode = request.ExternalSharingEnabled ? "external_sharing_enabled" : "external_sharing_disabled",
                CreatedAtUtc = now
            });
        }

        if (!string.Equals(normalizedAllowedGuestEmailDomainsCsv, previousAllowedDomainsCsv, StringComparison.Ordinal))
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = null,
                UserId = request.AdminUserId,
                EventType = "external_share_changed",
                ReasonCode = "external_share_guest_domain_allowlist_updated",
                CreatedAtUtc = now
            });
        }

        if (!string.Equals(normalizedBlockedGuestEmailsCsv, previousBlockedGuestEmailsCsv, StringComparison.Ordinal))
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = null,
                UserId = request.AdminUserId,
                EventType = "external_share_changed",
                ReasonCode = "external_share_guest_blocklist_updated",
                CreatedAtUtc = now
            });
        }

        if (request.MaxShareLinkLifetimeHours != previousMaxShareLinkLifetimeHours ||
            request.MaxShareLinkMaxUses != previousMaxShareLinkMaxUses ||
            request.MaxActiveShareLinksPerFile != previousMaxActiveShareLinksPerFile)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = null,
                UserId = request.AdminUserId,
                EventType = "external_share_changed",
                ReasonCode = "external_share_policy_limits_updated",
                CreatedAtUtc = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ExternalShareSettingsResponse.From(settings));
    }

    private static async Task<IResult> LockdownTenantExternalSharingAsync(
        LockdownExternalShareRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (request.AdminUserId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_admin_user_id"));
        }

        var now = DateTimeOffset.UtcNow;
        var settings = await dbContext.TenantExternalShareSettings
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId, cancellationToken);
        var previousEnabled = settings?.ExternalSharingEnabled ?? true;
        if (settings is null)
        {
            settings = new TenantExternalShareSettingsEntity
            {
                TenantId = request.TenantId
            };
            dbContext.TenantExternalShareSettings.Add(settings);
        }

        settings.ExternalSharingEnabled = false;
        settings.UpdatedAtUtc = now;
        settings.UpdatedByUserId = request.AdminUserId;

        var linksToRevoke = await dbContext.ExternalShareLinks
            .Where(candidate =>
                candidate.TenantId == request.TenantId &&
                !candidate.Revoked)
            .ToListAsync(cancellationToken);
        foreach (var link in linksToRevoke)
        {
            link.Revoked = true;
            link.RevokedAtUtc = now;
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = link.FileId,
                UserId = request.AdminUserId,
                EventType = "external_share_changed",
                ReasonCode = "external_share_link_revoked_by_lockdown",
                CreatedAtUtc = now
            });
        }

        if (previousEnabled)
        {
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = null,
                UserId = request.AdminUserId,
                EventType = "external_share_changed",
                ReasonCode = "external_sharing_disabled",
                CreatedAtUtc = now
            });
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = null,
            UserId = request.AdminUserId,
            EventType = "external_share_changed",
            ReasonCode = "external_share_tenant_lockdown",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ExternalShareLockdownResponse(
            request.TenantId,
            linksToRevoke.Count,
            now,
            ExternalShareSettingsResponse.From(settings)));
    }

    private sealed record UpsertExternalShareSettingsRequest(
        Guid TenantId,
        Guid AdminUserId,
        bool ExternalSharingEnabled,
        string? AllowedGuestEmailDomainsCsv,
        string? BlockedGuestEmailsCsv,
        int? MaxShareLinkLifetimeHours,
        int? MaxShareLinkMaxUses,
        int? MaxActiveShareLinksPerFile);

    private sealed record LockdownExternalShareRequest(
        Guid TenantId,
        Guid AdminUserId);

    private sealed record ExternalShareSettingsResponse(
        Guid TenantId,
        bool ExternalSharingEnabled,
        string AllowedGuestEmailDomainsCsv,
        string BlockedGuestEmailsCsv,
        int? MaxShareLinkLifetimeHours,
        int? MaxShareLinkMaxUses,
        int? MaxActiveShareLinksPerFile,
        DateTimeOffset? UpdatedAtUtc,
        Guid? UpdatedByUserId)
    {
        public static ExternalShareSettingsResponse Default(Guid tenantId)
            => new(tenantId, true, string.Empty, string.Empty, null, null, null, null, null);

        public static ExternalShareSettingsResponse From(TenantExternalShareSettingsEntity settings)
            => new(
                settings.TenantId,
                settings.ExternalSharingEnabled,
                settings.AllowedGuestEmailDomainsCsv ?? string.Empty,
                settings.BlockedGuestEmailsCsv ?? string.Empty,
                settings.MaxShareLinkLifetimeHours,
                settings.MaxShareLinkMaxUses,
                settings.MaxActiveShareLinksPerFile,
                settings.UpdatedAtUtc,
                settings.UpdatedByUserId);
    }

    private sealed record ExternalShareLockdownResponse(
        Guid TenantId,
        int RevokedShareLinkCount,
        DateTimeOffset LockedAtUtc,
        ExternalShareSettingsResponse Settings);

    private sealed record ExternalShareSettingsHistoryEventResponse(
        long Id,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc,
        string? UserEmail = null,
        string? UserDisplayName = null,
        string? FileContentType = null,
        bool? FileRevoked = null);

    private sealed record ExternalShareSettingsHistoryPageResponse(
        IReadOnlyList<ExternalShareSettingsHistoryEventResponse> Items,
        long? NextCursorId,
        bool HasMore);

    private sealed record ExternalShareSettingsHistorySummaryResponse(
        Guid TenantId,
        int WindowHours,
        int TotalEvents,
        int UniqueUsers,
        int TenantWideEvents,
        int FileScopedEvents,
        IReadOnlyList<ExternalShareSettingsHistoryReasonCountResponse> ReasonCounts,
        IReadOnlyList<ExternalShareSettingsHistoryUserCountResponse> TopUsers,
        IReadOnlyList<ExternalShareSettingsHistoryFileCountResponse> TopFiles,
        DateTimeOffset? LatestEventAtUtc,
        string? LatestEventReasonCode,
        Guid? LatestEventFileId,
        string? LatestEventFileContentType,
        bool? LatestEventFileRevoked,
        Guid? LatestEventUserId,
        string? LatestEventUserEmail,
        string? LatestEventUserDisplayName);

    private sealed record ExternalShareSettingsHistoryReasonCountResponse(
        string ReasonCode,
        int Count,
        int TenantWideCount,
        int FileScopedCount);

    private sealed record ExternalShareSettingsHistoryUserCountResponse(
        Guid UserId,
        string? Email,
        string? DisplayName,
        int Count,
        int TenantWideCount,
        int FileScopedCount);

    private sealed record ExternalShareSettingsHistoryFileCountResponse(
        Guid FileId,
        string? ContentType,
        bool? Revoked,
        int Count);

    private sealed record ExternalShareSettingsHistoryTrendPointResponse(
        string DayUtc,
        int TotalEvents,
        int UniqueUsers,
        int TenantWideEvents,
        int FileScopedEvents,
        IReadOnlyList<ExternalShareSettingsHistoryReasonCountResponse> ReasonCounts);

    private sealed record ErrorResponse(string ReasonCode);
}
