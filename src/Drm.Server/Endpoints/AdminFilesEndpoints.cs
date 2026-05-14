using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminFilesEndpoints
{
    public static IEndpointRouteBuilder MapAdminFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/files");

        group.MapGet("/", ListFilesAsync);
        group.MapPost("/{fileId:guid}/grants", UpsertGrantAsync);

        return endpoints;
    }

    private static async Task<IReadOnlyList<FileResponse>> ListFilesAsync(
        Guid tenantId,
        string? q,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProtectedFiles
            .AsNoTracking()
            .Where(file => file.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(file => file.ContentType.Contains(q));
        }

        return await query
            .OrderBy(file => file.Id)
            .Take(100)
            .Select(file => FileResponse.From(file))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Created<FileGrantResponse>, BadRequest<ErrorResponse>, NotFound>> UpsertGrantAsync(
        Guid fileId,
        UpsertFileGrantRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GrantSubjectType>(request.SubjectType, ignoreCase: true, out var subjectType)
            || !Enum.IsDefined(subjectType))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_subject_type"));
        }

        if (!PermissionParser.TryParse(request.Permissions, out var permissions))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
        }

        if (!await FileExistsAsync(dbContext, request.TenantId, fileId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (subjectType == GrantSubjectType.Group
            && !await GroupExistsAsync(dbContext, request.TenantId, request.SubjectId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var normalizedSubjectType = subjectType.ToString();
        var normalizedPermissions = permissions.ToString();

        var grant = await dbContext.FileGrants.SingleOrDefaultAsync(
            candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.FileId == fileId &&
                candidate.SubjectType == normalizedSubjectType &&
                candidate.SubjectId == request.SubjectId,
            cancellationToken);

        var createdGrant = grant is null;
        if (grant is null)
        {
            grant = new FileGrantEntity
            {
                TenantId = request.TenantId,
                FileId = fileId,
                SubjectType = normalizedSubjectType,
                SubjectId = request.SubjectId,
                Permissions = normalizedPermissions,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.FileGrants.Add(grant);
        }
        else
        {
            grant.Permissions = normalizedPermissions;
        }

        dbContext.AuditEvents.Add(AdminAudit.PermissionEvent(request.TenantId, fileId, null, "file_grant_upserted"));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!createdGrant)
            {
                throw;
            }

            dbContext.Entry(grant).State = EntityState.Detached;

            var conflictingGrant = await FindGrantAsync(
                dbContext,
                request.TenantId,
                fileId,
                normalizedSubjectType,
                request.SubjectId,
                cancellationToken);

            if (conflictingGrant is null)
            {
                throw;
            }

            conflictingGrant.Permissions = normalizedPermissions;
            await dbContext.SaveChangesAsync(cancellationToken);
            grant = conflictingGrant;
        }

        return TypedResults.Created(
            $"/api/admin/files/{fileId}/grants/{grant.SubjectType}/{grant.SubjectId}",
            FileGrantResponse.From(grant));
    }

    private static Task<FileGrantEntity?> FindGrantAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid fileId,
        string subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        return dbContext.FileGrants.SingleOrDefaultAsync(
            candidate =>
                candidate.TenantId == tenantId &&
                candidate.FileId == fileId &&
                candidate.SubjectType == subjectType &&
                candidate.SubjectId == subjectId,
            cancellationToken);
    }

    private static Task<bool> FileExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        return dbContext.ProtectedFiles
            .AsNoTracking()
            .AnyAsync(file => file.TenantId == tenantId && file.Id == fileId, cancellationToken);
    }

    private static Task<bool> GroupExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        return dbContext.TenantGroups
            .AsNoTracking()
            .AnyAsync(group => group.TenantId == tenantId && group.GroupId == groupId, cancellationToken);
    }

    private sealed record UpsertFileGrantRequest(
        Guid TenantId,
        string SubjectType,
        Guid SubjectId,
        string Permissions);

    private sealed record FileGrantResponse(
        Guid TenantId,
        Guid FileId,
        string SubjectType,
        Guid SubjectId,
        string Permissions)
    {
        public static FileGrantResponse From(FileGrantEntity grant)
            => new(
                grant.TenantId,
                grant.FileId,
                grant.SubjectType,
                grant.SubjectId,
                grant.Permissions);
    }

    private sealed record FileResponse(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string WatermarkTemplate)
    {
        public static FileResponse From(ProtectedFileEntity file)
            => new(
                file.TenantId,
                file.Id,
                file.OwnerUserId,
                file.ContentType,
                file.ExpiresAtUtc,
                file.Permissions.ToString(),
                file.WatermarkTemplate);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
