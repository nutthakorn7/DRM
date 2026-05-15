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
        group.MapPost("/{fileId:guid}/commands/delete-protected-copy", EnqueueDeleteProtectedCopyCommandAsync);
        group.MapPost("/{fileId:guid}/grants", UpsertGrantAsync);
        group.MapPut("/{fileId:guid}/grants", ReplaceGrantsAsync);
        group.MapPost("/{fileId:guid}/apply-policy-template", ApplyPolicyTemplateAsync);
        group.MapPost("/{fileId:guid}/revoke", RevokeFileAsync);

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

    private static async Task<Results<Created<AgentCommandResponse>, NotFound>> EnqueueDeleteProtectedCopyCommandAsync(
        Guid fileId,
        EnqueueDeleteProtectedCopyCommandRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await FileExistsAsync(dbContext, request.TenantId, fileId, cancellationToken) ||
            !await DeviceExistsAsync(dbContext, request.TenantId, request.DeviceId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var command = new AgentCommandEntity
        {
            TenantId = request.TenantId,
            CommandId = Guid.NewGuid(),
            DeviceId = request.DeviceId,
            FileId = fileId,
            CommandType = "DeleteProtectedCopy",
            Status = "Pending",
            ReasonCode = "queued",
            CreatedAtUtc = now
        };

        dbContext.AgentCommands.Add(command);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = fileId,
            UserId = request.AdminUserId,
            EventType = "protected_file_delete_requested",
            ReasonCode = "queued",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/files/{fileId}/commands/{command.CommandId}",
            AgentCommandResponse.From(command));
    }

    private static async Task<Results<Ok<RevokeFileResponse>, NotFound>> RevokeFileAsync(
        Guid fileId,
        RevokeFileRequest request,
        AppDbContext dbContext,
        ISiemDispatcher siemDispatcher,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId && candidate.Id == fileId, cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        file.Revoked = true;
        var auditEvent = new AuditEventEntity
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            UserId = request.AdminUserId,
            EventType = "file_revoked",
            ReasonCode = "revoked",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.AuditEvents.Add(auditEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await siemDispatcher.DispatchAsync(auditEvent, cancellationToken);

        return TypedResults.Ok(new RevokeFileResponse(file.TenantId, file.Id, file.Revoked));
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

    private static async Task<Results<Ok<IReadOnlyList<FileGrantResponse>>, BadRequest<ErrorResponse>, NotFound>> ReplaceGrantsAsync(
        Guid fileId,
        ReplaceGrantsRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId && candidate.Id == fileId, cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Grants is null)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_grants"));
        }

        var parsed = new List<FileGrantEntity>();
        var seenSubjects = new HashSet<(string SubjectType, Guid SubjectId)>();
        foreach (var grant in request.Grants)
        {
            if (grant is null)
            {
                return TypedResults.BadRequest(new ErrorResponse("invalid_grants"));
            }

            if (!Enum.TryParse<GrantSubjectType>(grant.SubjectType, ignoreCase: true, out var subjectType)
                || !Enum.IsDefined(subjectType))
            {
                return TypedResults.BadRequest(new ErrorResponse("invalid_subject_type"));
            }

            if (!PermissionParser.TryParse(grant.Permissions, out var permissions))
            {
                return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
            }

            if (subjectType == GrantSubjectType.Group
                && !await GroupExistsAsync(dbContext, request.TenantId, grant.SubjectId, cancellationToken))
            {
                return TypedResults.NotFound();
            }

            var normalizedSubjectType = subjectType.ToString();
            if (!seenSubjects.Add((normalizedSubjectType, grant.SubjectId)))
            {
                return TypedResults.BadRequest(new ErrorResponse("duplicate_grant"));
            }

            parsed.Add(new FileGrantEntity
            {
                TenantId = request.TenantId,
                FileId = fileId,
                SubjectType = normalizedSubjectType,
                SubjectId = grant.SubjectId,
                Permissions = permissions.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        var existing = await dbContext.FileGrants
            .Where(grant => grant.TenantId == request.TenantId && grant.FileId == fileId)
            .ToListAsync(cancellationToken);

        dbContext.FileGrants.RemoveRange(existing);
        dbContext.FileGrants.AddRange(parsed);
        file.Permissions = Permission.None;
        dbContext.AuditEvents.Add(AdminAudit.PermissionEvent(request.TenantId, fileId, null, "file_grants_replaced"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<FileGrantResponse>>(parsed
            .Select(FileGrantResponse.From)
            .ToList());
    }

    private static async Task<Results<Ok<FileResponse>, NotFound>> ApplyPolicyTemplateAsync(
        Guid fileId,
        ApplyPolicyTemplateRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.Id == fileId,
                cancellationToken);
        var template = await dbContext.PolicyTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.TemplateId == request.TemplateId,
                cancellationToken);

        if (file is null || template is null)
        {
            return TypedResults.NotFound();
        }

        if (!PermissionParser.TryParse(template.Permissions, out var permissions))
        {
            return TypedResults.NotFound();
        }

        file.Permissions = permissions;
        file.WatermarkTemplate = template.WatermarkTemplate;
        file.OfflineLeaseMinutes = template.OfflineLeaseMinutes;
        await UpsertOwnerGrantFromTemplateAsync(dbContext, file, permissions, cancellationToken);

        dbContext.AuditEvents.Add(AdminAudit.PermissionEvent(
            request.TenantId,
            fileId,
            request.AdminUserId,
            "policy_template_applied"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(FileResponse.From(file));
    }

    private static async Task UpsertOwnerGrantFromTemplateAsync(
        AppDbContext dbContext,
        ProtectedFileEntity file,
        Permission permissions,
        CancellationToken cancellationToken)
    {
        var subjectType = GrantSubjectType.User.ToString();
        var grant = await dbContext.FileGrants.SingleOrDefaultAsync(
            candidate =>
                candidate.TenantId == file.TenantId &&
                candidate.FileId == file.Id &&
                candidate.SubjectType == subjectType &&
                candidate.SubjectId == file.OwnerUserId,
            cancellationToken);

        if (grant is null)
        {
            dbContext.FileGrants.Add(new FileGrantEntity
            {
                TenantId = file.TenantId,
                FileId = file.Id,
                SubjectType = subjectType,
                SubjectId = file.OwnerUserId,
                Permissions = permissions.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        grant.Permissions = permissions.ToString();
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

    private static Task<bool> DeviceExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        return dbContext.AgentDevices
            .AsNoTracking()
            .AnyAsync(device => device.TenantId == tenantId && device.DeviceId == deviceId, cancellationToken);
    }

    private sealed record EnqueueDeleteProtectedCopyCommandRequest(Guid TenantId, Guid DeviceId, Guid AdminUserId);

    private sealed record RevokeFileRequest(Guid TenantId, Guid AdminUserId);

    private sealed record UpsertFileGrantRequest(
        Guid TenantId,
        string SubjectType,
        Guid SubjectId,
        string Permissions);

    private sealed record ReplaceGrantsRequest(Guid TenantId, IReadOnlyList<ReplaceGrantItem?>? Grants);

    private sealed record ReplaceGrantItem(string SubjectType, Guid SubjectId, string Permissions);

    private sealed record ApplyPolicyTemplateRequest(Guid TenantId, Guid TemplateId, Guid AdminUserId);

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
        string WatermarkTemplate,
        bool Revoked)
    {
        public static FileResponse From(ProtectedFileEntity file)
            => new(
                file.TenantId,
                file.Id,
                file.OwnerUserId,
                file.ContentType,
                file.ExpiresAtUtc,
                file.Permissions.ToString(),
                file.WatermarkTemplate,
                file.Revoked);
    }

    private sealed record RevokeFileResponse(Guid TenantId, Guid FileId, bool Revoked);

    private sealed record AgentCommandResponse(
        Guid TenantId,
        Guid CommandId,
        Guid DeviceId,
        Guid FileId,
        string CommandType,
        string Status,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc)
    {
        public static AgentCommandResponse From(AgentCommandEntity command)
            => new(
                command.TenantId,
                command.CommandId,
                command.DeviceId,
                command.FileId,
                command.CommandType,
                command.Status,
                command.ReasonCode,
                command.CreatedAtUtc,
                command.CompletedAtUtc);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
