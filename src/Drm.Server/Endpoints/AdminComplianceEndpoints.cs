using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Drm.Server.Endpoints;

public static class AdminComplianceEndpoints
{
    public static IEndpointRouteBuilder MapAdminComplianceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/tenants/{tenantId:guid}/compliance/export", ExportAsync);
        endpoints.MapDelete("/api/admin/tenants/{tenantId:guid}/users/{userId:guid}/data", EraseUserDataAsync);
        return endpoints;
    }

    private static async Task<IResult> ExportAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsRead, tenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        var files = await dbContext.ProtectedFiles.AsNoTracking()
            .Where(f => f.TenantId == tenantId)
            .ToListAsync(ct);

        var auditEvents = await dbContext.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        var chainKeyHex = configuration["Drm:Security:AuditChainKey"] ?? string.Empty;
        var chainResult = await AuditChainService.VerifyTenantChainAsync(dbContext, tenantId, chainKeyHex, ct);

        var generatedAt = DateTimeOffset.UtcNow;

        var manifest = new
        {
            TenantId = tenantId,
            TenantName = tenant.DisplayName,
            GeneratedAtUtc = generatedAt,
            TotalFiles = files.Count,
            TotalAuditEvents = auditEvents.Count,
            SchemaVersion = "1.0"
        };

        var fileRows = files.Select(f => new
        {
            f.Id, f.TenantId, f.OwnerUserId, f.ContentType,
            f.ExpiresAtUtc, f.Revoked, Permissions = f.Permissions.ToString()
        });

        var auditRows = auditEvents.Select(e => new
        {
            e.Id, e.TenantId, e.FileId, e.UserId, e.EventType, e.ReasonCode, e.CreatedAtUtc
        });

        var chainJson = new
        {
            chainResult.Valid,
            chainResult.EventsChecked,
            chainResult.FailureReason
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonEntryAsync(zip, "manifest.json", manifest);
            await WriteJsonEntryAsync(zip, "files.json", fileRows);
            await WriteJsonEntryAsync(zip, "audit-events.json", auditRows);
            await WriteJsonEntryAsync(zip, "chain-verification.json", chainJson);
        }

        var fileName = $"compliance-{tenantId:N}-{generatedAt:yyyyMMddHHmmss}.zip";
        return Results.File(ms.ToArray(), "application/zip", fileName);
    }

    private static async Task WriteJsonEntryAsync(ZipArchive zip, string entryName, object data)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });
        await stream.WriteAsync(bytes);
    }

    private static async Task<IResult> EraseUserDataAsync(
        Guid tenantId,
        Guid userId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var user = await dbContext.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == userId, ct);
        if (user is null) return Results.NotFound();

        var originalEmail = user.Email;

        // 1. Anonymize user record
        user.Email = $"erased-{userId:N}@gdpr.invalid";
        user.DisplayName = "Erased User";
        user.Active = false;

        // 2. Delete file grants for this user
        var grants = await dbContext.FileGrants
            .Where(g => g.TenantId == tenantId && g.SubjectType == "user" && g.SubjectId == userId)
            .ToListAsync(ct);
        dbContext.FileGrants.RemoveRange(grants);

        // 3. Anonymize audit events (tombstone UserId)
        var events = await dbContext.AuditEvents
            .Where(e => e.TenantId == tenantId && e.UserId == userId)
            .ToListAsync(ct);
        foreach (var evt in events)
            evt.UserId = Guid.Empty;

        // 4. Delete file access requests for this user's email
        var requests = await dbContext.FileAccessRequests
            .Where(r => r.TenantId == tenantId && r.RequesterEmail == originalEmail)
            .ToListAsync(ct);
        dbContext.FileAccessRequests.RemoveRange(requests);

        // 5. Emit erasure audit event
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = Guid.Empty,
            UserId = Guid.Empty,
            EventType = "user_data_erased",
            ReasonCode = "gdpr_erasure",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            UserId = userId,
            UsersAnonymized = 1,
            GrantsDeleted = grants.Count,
            AuditEventsAnonymized = events.Count,
            AccessRequestsDeleted = requests.Count
        });
    }
}
