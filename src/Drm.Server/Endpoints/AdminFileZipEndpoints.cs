using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminFileZipEndpoints
{
    public static IEndpointRouteBuilder MapAdminFileZipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/files/{fileId:guid}/convert/zip", ConvertToZipAsync);
        return endpoints;
    }

    private static async Task<IResult> ConvertToZipAsync(
        Guid fileId,
        Guid tenantId,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.FilesZip, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (tenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        var file = await dbContext.ProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fileId && f.TenantId == tenantId, cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        var tags = await dbContext.FileTags
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.FileId == fileId)
            .Select(t => t.Tag)
            .ToListAsync(cancellationToken);

        var grants = await dbContext.FileGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.FileId == fileId)
            .Select(g => new { g.SubjectType, g.SubjectId, g.Permissions })
            .ToListAsync(cancellationToken);

        var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var manifest = new
        {
            fileId = file.Id,
            tenantId = file.TenantId,
            ownerUserId = file.OwnerUserId,
            contentType = file.ContentType,
            expiresAtUtc = file.ExpiresAtUtc,
            revoked = file.Revoked,
            permissions = file.Permissions.ToString(),
            watermarkTemplate = file.WatermarkTemplate,
            offlineLeaseMinutes = file.OfflineLeaseMinutes,
            tags,
            grants,
            generatedAtUtc = DateTimeOffset.UtcNow
        };

        var readme = BuildReadme(file, origin);
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(archive, "README.txt", readme, cancellationToken);
            await WriteEntryAsync(archive, "manifest.json", manifestJson, cancellationToken);
            await WriteEntryAsync(archive, $"share-link.txt", $"{origin}/share/?fileId={file.Id}\n", cancellationToken);
        }
        memoryStream.Position = 0;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = file.Id,
            ActorAdminId = AdminAudit.ActorId(httpContext),
            EventType = "system_changed",
            ReasonCode = "file_converted_to_zip",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.File(
            memoryStream,
            contentType: "application/zip",
            fileDownloadName: $"drm-{file.Id:N}.zip");
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    private static string BuildReadme(ProtectedFileEntity file, string origin)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DRM Protected File");
        sb.AppendLine("==================");
        sb.AppendLine();
        sb.AppendLine($"File ID:        {file.Id}");
        sb.AppendLine($"Content type:   {file.ContentType}");
        sb.AppendLine($"Expires (UTC):  {file.ExpiresAtUtc:O}");
        sb.AppendLine($"Permissions:    {file.Permissions}");
        sb.AppendLine($"Revoked:        {file.Revoked}");
        sb.AppendLine();
        sb.AppendLine("This archive contains:");
        sb.AppendLine("  - README.txt       This file");
        sb.AppendLine("  - manifest.json    Structured metadata (grants, tags, policy)");
        sb.AppendLine("  - share-link.txt   Browser viewer URL");
        sb.AppendLine();
        sb.AppendLine("To open the protected file:");
        sb.AppendLine("  1. Install the DRM Client from your administrator, OR");
        sb.AppendLine("  2. Open the share-link.txt URL in a browser and complete email verification.");
        sb.AppendLine();
        sb.AppendLine($"Server: {origin}");
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        return sb.ToString();
    }

    private sealed record ErrorResponse(string ReasonCode);
}
