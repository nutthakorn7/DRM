using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class FilesEndpoints
{
    private const string DefaultWatermarkTemplate = "{user} {time} {file}";

    public static IEndpointRouteBuilder MapFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/files");

        group.MapPost("/", RegisterFileAsync);
        group.MapPost("/{fileId:guid}/revoke", RevokeFileAsync);

        return endpoints;
    }

    private static async Task<Results<Created<RegisterFileResponse>, Conflict, BadRequest<ErrorResponse>, NotFound>> RegisterFileAsync(
        RegisterFileRequest request,
        AppDbContext dbContext,
        ISiemDispatcher siemDispatcher,
        CancellationToken cancellationToken)
    {
        if (!PermissionParser.TryParse(request.Permissions, out var permissions))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
        }

        var duplicateExists = await dbContext.ProtectedFiles
            .AnyAsync(candidate => candidate.TenantId == request.TenantId && candidate.Id == request.FileId, cancellationToken);
        if (duplicateExists)
        {
            return TypedResults.Conflict();
        }

        var effectivePolicy = await BuildEffectivePolicyAsync(request, dbContext, permissions, cancellationToken);
        if (effectivePolicy is null)
        {
            return TypedResults.NotFound();
        }

        permissions = effectivePolicy.Permissions;
        var watermarkTemplate = effectivePolicy.WatermarkTemplate;
        var grants = new List<FileGrantEntity>();
        var recipientError = await BuildFileGrantsAsync(request, dbContext, permissions, grants, cancellationToken);
        if (recipientError is not null)
        {
            if (recipientError == RecipientBuildError.NotFound)
            {
                return TypedResults.NotFound();
            }

            var reasonCode = recipientError switch
            {
                RecipientBuildError.InvalidRecipient => "invalid_recipient",
                RecipientBuildError.InvalidSubjectType => "invalid_subject_type",
                RecipientBuildError.DuplicateRecipient => "duplicate_recipient",
                _ => throw new InvalidOperationException($"Unknown recipient build error '{recipientError}'.")
            };
            return TypedResults.BadRequest(new ErrorResponse(reasonCode));
        }

        var file = new ProtectedFileEntity
        {
            Id = request.FileId,
            TenantId = request.TenantId,
            OwnerUserId = request.OwnerUserId,
            ContentType = request.ContentType,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Revoked = false,
            Permissions = permissions,
            WatermarkTemplate = watermarkTemplate
        };

        dbContext.ProtectedFiles.Add(file);
        dbContext.FileGrants.AddRange(grants);
        var auditEvent = new AuditEventEntity
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            UserId = file.OwnerUserId,
            EventType = "file_registered",
            ReasonCode = "registered",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.AuditEvents.Add(auditEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await ProtectedFileExistsAsync(dbContext, file.TenantId, file.Id, cancellationToken))
            {
                return TypedResults.Conflict();
            }

            throw;
        }

        await siemDispatcher.DispatchAsync(auditEvent, cancellationToken);

        return TypedResults.Created($"/api/files/{file.Id}", new RegisterFileResponse(
            file.Id,
            file.TenantId,
            file.OwnerUserId,
            file.ContentType,
            file.ExpiresAtUtc,
            file.Permissions.ToString(),
            file.WatermarkTemplate));
    }

    private static async Task<EffectiveRegistrationPolicy?> BuildEffectivePolicyAsync(
        RegisterFileRequest request,
        AppDbContext dbContext,
        Permission fallbackPermissions,
        CancellationToken cancellationToken)
    {
        if (request.PolicyTemplateId is null)
        {
            return new EffectiveRegistrationPolicy(
                fallbackPermissions,
                request.WatermarkTemplate ?? DefaultWatermarkTemplate);
        }

        var template = await dbContext.PolicyTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.TemplateId == request.PolicyTemplateId.Value,
                cancellationToken);

        if (template is null)
        {
            return null;
        }

        if (!PermissionParser.TryParse(template.Permissions, out var templatePermissions))
        {
            return null;
        }

        return new EffectiveRegistrationPolicy(templatePermissions, template.WatermarkTemplate);
    }

    private static async Task<RecipientBuildError?> BuildFileGrantsAsync(
        RegisterFileRequest request,
        AppDbContext dbContext,
        Permission permissions,
        List<FileGrantEntity> grants,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerSubjectType = GrantSubjectType.User.ToString();
        var seenRecipients = new HashSet<(string SubjectType, Guid SubjectId)> { (ownerSubjectType, request.OwnerUserId) };

        grants.Add(new FileGrantEntity
        {
            TenantId = request.TenantId,
            FileId = request.FileId,
            SubjectType = ownerSubjectType,
            SubjectId = request.OwnerUserId,
            Permissions = permissions.ToString(),
            CreatedAtUtc = now
        });

        if (request.Recipients is null)
        {
            return null;
        }

        foreach (var recipient in request.Recipients)
        {
            if (recipient is null || recipient.SubjectId == Guid.Empty)
            {
                return RecipientBuildError.InvalidRecipient;
            }

            if (!Enum.TryParse<GrantSubjectType>(recipient.SubjectType, ignoreCase: true, out var subjectType)
                || !Enum.IsDefined(subjectType))
            {
                return RecipientBuildError.InvalidSubjectType;
            }

            if (subjectType == GrantSubjectType.Group
                && !await GroupExistsAsync(dbContext, request.TenantId, recipient.SubjectId, cancellationToken))
            {
                return RecipientBuildError.NotFound;
            }

            var normalizedSubjectType = subjectType.ToString();
            if (!seenRecipients.Add((normalizedSubjectType, recipient.SubjectId)))
            {
                return RecipientBuildError.DuplicateRecipient;
            }

            grants.Add(new FileGrantEntity
            {
                TenantId = request.TenantId,
                FileId = request.FileId,
                SubjectType = normalizedSubjectType,
                SubjectId = recipient.SubjectId,
                Permissions = permissions.ToString(),
                CreatedAtUtc = now
            });
        }

        return null;
    }

    private static async Task<Results<Ok<RevokeFileResponse>, NotFound>> RevokeFileAsync(
        Guid fileId,
        Guid tenantId,
        AppDbContext dbContext,
        ISiemDispatcher siemDispatcher,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == fileId, cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        file.Revoked = true;
        var auditEvent = new AuditEventEntity
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            UserId = file.OwnerUserId,
            EventType = "file_revoked",
            ReasonCode = "revoked",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.AuditEvents.Add(auditEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await siemDispatcher.DispatchAsync(auditEvent, cancellationToken);

        return TypedResults.Ok(new RevokeFileResponse(file.Id, file.Revoked));
    }

    private static Task<bool> ProtectedFileExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        return dbContext.ProtectedFiles
            .AsNoTracking()
            .AnyAsync(candidate => candidate.TenantId == tenantId && candidate.Id == fileId, cancellationToken);
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

    private sealed record RegisterFileRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string? WatermarkTemplate,
        Guid? PolicyTemplateId = null,
        IReadOnlyList<RegisterFileRecipientRequest?>? Recipients = null);

    private sealed record RegisterFileRecipientRequest(string SubjectType, Guid SubjectId);

    private sealed record EffectiveRegistrationPolicy(Permission Permissions, string WatermarkTemplate);

    private sealed record RegisterFileResponse(
        Guid FileId,
        Guid TenantId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string WatermarkTemplate);

    private sealed record RevokeFileResponse(Guid FileId, bool Revoked);

    private sealed record ErrorResponse(string ReasonCode);

    private enum RecipientBuildError
    {
        NotFound,
        InvalidRecipient,
        InvalidSubjectType,
        DuplicateRecipient
    }
}
