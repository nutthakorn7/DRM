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
        group.MapGet("/{fileId:guid}/commands", ListFileCommandsAsync);
        group.MapPost("/{fileId:guid}/commands/delete-protected-copy", EnqueueDeleteProtectedCopyCommandAsync);
        group.MapPost("/{fileId:guid}/share-links", CreateExternalShareLinkAsync);
        group.MapGet("/{fileId:guid}/share-links", ListExternalShareLinksAsync);
        group.MapPost("/{fileId:guid}/share-links/{shareLinkId:guid}/revoke", RevokeExternalShareLinkAsync);
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

    private static async Task<Results<Ok<IReadOnlyList<AgentCommandResponse>>, NotFound>> ListFileCommandsAsync(
        Guid fileId,
        Guid tenantId,
        Guid? deviceId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await FileExistsAsync(dbContext, tenantId, fileId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var query = dbContext.AgentCommands
            .AsNoTracking()
            .Where(command => command.TenantId == tenantId && command.FileId == fileId);

        if (deviceId is not null)
        {
            query = query.Where(command => command.DeviceId == deviceId.Value);
        }

        var commands = await query
            .Take(100)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<AgentCommandResponse>>(commands
            .OrderByDescending(command => command.CreatedAtUtc)
            .Select(AgentCommandResponse.From)
            .ToList());
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

    private static async Task<Results<Created<CreateExternalShareLinkResponse>, BadRequest<ErrorResponse>, NotFound>> CreateExternalShareLinkAsync(
        Guid fileId,
        CreateExternalShareLinkRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        IAdminNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.Id == fileId,
                cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var validationError = ValidateCreateExternalShareLinkRequest(file, request, now);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        var token = ExternalShareToken.Create();
        var shareLink = new ExternalShareLinkEntity
        {
            TenantId = request.TenantId,
            ShareLinkId = Guid.NewGuid(),
            FileId = fileId,
            TokenHash = token.Hash,
            GuestEmail = NormalizeEmail(request.GuestEmail),
            ExpiresAtUtc = request.ExpiresAtUtc,
            MaxUses = request.MaxUses,
            UsedCount = 0,
            Revoked = false,
            CreatedAtUtc = now
        };

        dbContext.ExternalShareLinks.Add(shareLink);
        dbContext.AuditEvents.Add(ExternalShareAuditEvent(
            request.TenantId,
            fileId,
            request.AdminUserId,
            "external_share_link_created",
            now));

        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService.NotifyAsync(
            request.TenantId,
            new AdminNotificationEvent(
                "external_share_link_created",
                fileId,
                request.AdminUserId,
                NormalizeEmail(request.GuestEmail),
                now),
            cancellationToken);

        var shareUrl = BuildExternalShareUrl(
            httpContext,
            request.TenantId,
            token.Plaintext,
            shareLink.GuestEmail);

        return TypedResults.Created(
            $"/api/admin/files/{fileId}/share-links/{shareLink.ShareLinkId}",
            CreateExternalShareLinkResponse.From(shareLink, token.Plaintext, shareUrl));
    }

    private static async Task<Results<Ok<IReadOnlyList<ExternalShareLinkResponse>>, NotFound>> ListExternalShareLinksAsync(
        Guid fileId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await FileExistsAsync(dbContext, tenantId, fileId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var links = await dbContext.ExternalShareLinks
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.FileId == fileId)
            .Take(100)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<ExternalShareLinkResponse>>(links
            .OrderByDescending(link => link.CreatedAtUtc)
            .ThenBy(link => link.ShareLinkId)
            .Select(ExternalShareLinkResponse.From)
            .ToList());
    }

    private static async Task<Results<Ok<ExternalShareLinkResponse>, NotFound>> RevokeExternalShareLinkAsync(
        Guid fileId,
        Guid shareLinkId,
        RevokeExternalShareLinkRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var shareLink = await dbContext.ExternalShareLinks
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.TenantId == request.TenantId &&
                    candidate.FileId == fileId &&
                    candidate.ShareLinkId == shareLinkId,
                cancellationToken);

        if (shareLink is null)
        {
            return TypedResults.NotFound();
        }

        if (!shareLink.Revoked)
        {
            var now = DateTimeOffset.UtcNow;
            shareLink.Revoked = true;
            shareLink.RevokedAtUtc = now;
            dbContext.AuditEvents.Add(ExternalShareAuditEvent(
                request.TenantId,
                fileId,
                request.AdminUserId,
                "external_share_link_revoked",
                now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(ExternalShareLinkResponse.From(shareLink));
    }

    private static async Task<Results<Ok<RevokeFileResponse>, NotFound>> RevokeFileAsync(
        Guid fileId,
        RevokeFileRequest request,
        AppDbContext dbContext,
        ISiemDispatcher siemDispatcher,
        IAdminNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId && candidate.Id == fileId, cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        file.Revoked = true;
        var auditEvent = new AuditEventEntity
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            UserId = request.AdminUserId,
            EventType = "file_revoked",
            ReasonCode = "revoked",
            CreatedAtUtc = now
        };
        dbContext.AuditEvents.Add(auditEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await siemDispatcher.DispatchAsync(auditEvent, cancellationToken);
        await notificationService.NotifyAsync(
            file.TenantId,
            new AdminNotificationEvent(
                "file_revoked",
                file.Id,
                request.AdminUserId,
                null,
                now),
            cancellationToken);

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

    private static ErrorResponse? ValidateCreateExternalShareLinkRequest(
        ProtectedFileEntity file,
        CreateExternalShareLinkRequest request,
        DateTimeOffset now)
    {
        if (file.Revoked)
        {
            return new ErrorResponse("file_revoked");
        }

        if (!IsValidGuestEmail(request.GuestEmail))
        {
            return new ErrorResponse("invalid_guest_email");
        }

        if (request.ExpiresAtUtc <= now)
        {
            return new ErrorResponse("invalid_expires_at");
        }

        if (request.ExpiresAtUtc > file.ExpiresAtUtc)
        {
            return new ErrorResponse("share_link_exceeds_file_expiry");
        }

        if (request.MaxUses is < 1 or > 1000)
        {
            return new ErrorResponse("invalid_max_uses");
        }

        return null;
    }

    private static bool IsValidGuestEmail(string? email)
    {
        var normalized = NormalizeEmail(email);
        var atIndex = normalized.IndexOf('@');
        return normalized.Length is >= 3 and <= 320
            && atIndex > 0
            && atIndex == normalized.LastIndexOf('@')
            && atIndex < normalized.Length - 1
            && normalized.IndexOfAny([' ', '\t', '\r', '\n']) < 0;
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static AuditEventEntity ExternalShareAuditEvent(
        Guid tenantId,
        Guid fileId,
        Guid adminUserId,
        string reasonCode,
        DateTimeOffset createdAtUtc)
    {
        return new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = adminUserId,
            EventType = "external_share_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static string BuildExternalShareUrl(
        HttpContext httpContext,
        Guid tenantId,
        string accessToken,
        string guestEmail)
    {
        var query = QueryString.Create([
            new KeyValuePair<string, string?>("tenantId", tenantId.ToString()),
            new KeyValuePair<string, string?>("accessToken", accessToken),
            new KeyValuePair<string, string?>("guestEmail", guestEmail)
        ]);
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/share/{query}";
    }

    private sealed record EnqueueDeleteProtectedCopyCommandRequest(Guid TenantId, Guid DeviceId, Guid AdminUserId);

    private sealed record RevokeFileRequest(Guid TenantId, Guid AdminUserId);

    private sealed record CreateExternalShareLinkRequest(
        Guid TenantId,
        Guid AdminUserId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses);

    private sealed record RevokeExternalShareLinkRequest(Guid TenantId, Guid AdminUserId);

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

    private sealed record CreateExternalShareLinkResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        bool Revoked,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? RevokedAtUtc,
        string AccessToken,
        string ShareUrl)
    {
        public static CreateExternalShareLinkResponse From(ExternalShareLinkEntity link, string accessToken, string shareUrl)
            => new(
                link.TenantId,
                link.ShareLinkId,
                link.FileId,
                link.GuestEmail,
                link.ExpiresAtUtc,
                link.MaxUses,
                link.UsedCount,
                link.Revoked,
                link.CreatedAtUtc,
                link.RevokedAtUtc,
                accessToken,
                shareUrl);
    }

    private sealed record ExternalShareLinkResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        bool Revoked,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? RevokedAtUtc)
    {
        public static ExternalShareLinkResponse From(ExternalShareLinkEntity link)
            => new(
                link.TenantId,
                link.ShareLinkId,
                link.FileId,
                link.GuestEmail,
                link.ExpiresAtUtc,
                link.MaxUses,
                link.UsedCount,
                link.Revoked,
                link.CreatedAtUtc,
                link.RevokedAtUtc);
    }

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
