using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class OutlookAddInEndpoints
{
    private const long MaxAttachmentBytes = 100 * 1024 * 1024;

    public static IEndpointRouteBuilder MapOutlookAddInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/outlook");

        group.MapPost("/protect-attachment", ProtectAttachmentAsync);
        group.MapGet("/status", GetStatusAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<ProtectAttachmentResponse>, NotFound, BadRequest<ErrorResponse>>> ProtectAttachmentAsync(
        ProtectAttachmentRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (string.IsNullOrWhiteSpace(request.AttachmentName))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_attachment_name"));
        }

        if (request.AttachmentSizeBytes <= 0 || request.AttachmentSizeBytes > MaxAttachmentBytes)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_attachment_size"));
        }

        var config = await dbContext.TenantOutlookIntegrationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);

        if (config is null || !config.Enabled)
        {
            return TypedResults.NotFound();
        }

        var skipDomains = ParseDomains(config.SkipDomainsCsv);
        var status = "protected";
        string? protectedFileId = null;

        if (config.MinAttachmentSizeKb > 0 && request.AttachmentSizeBytes < config.MinAttachmentSizeKb * 1024L)
        {
            status = "skipped_below_min_size";
        }
        else if (AnyRecipientInSkipDomains(request.Recipients, skipDomains))
        {
            status = "skipped_recipient_domain";
        }
        else if (!config.AutoEncryptOutgoingAttachments)
        {
            status = "skipped_auto_disabled";
        }
        else
        {
            protectedFileId = Guid.NewGuid().ToString("N");
            config.LifetimeProtectedCount++;
        }

        dbContext.OutlookAttachmentEvents.Add(new OutlookAttachmentEventEntity
        {
            TenantId = request.TenantId,
            SenderEmail = request.SenderEmail ?? string.Empty,
            RecipientCsv = string.Join(",", request.Recipients ?? Array.Empty<string>()),
            AttachmentName = request.AttachmentName,
            AttachmentSizeBytes = request.AttachmentSizeBytes,
            Status = status,
            ProtectedFileId = protectedFileId,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ProtectAttachmentResponse(status, protectedFileId));
    }

    private static async Task<Ok<OutlookAddInStatusResponse>> GetStatusAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.TenantOutlookIntegrationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            return TypedResults.Ok(new OutlookAddInStatusResponse(false, false, 0, 0));
        }

        return TypedResults.Ok(new OutlookAddInStatusResponse(
            config.Enabled,
            config.AutoEncryptOutgoingAttachments,
            config.MinAttachmentSizeKb,
            config.LifetimeProtectedCount));
    }

    private static HashSet<string> ParseDomains(string csv)
    {
        return csv
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant().TrimStart('@'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool AnyRecipientInSkipDomains(IReadOnlyList<string>? recipients, HashSet<string> skipDomains)
    {
        if (recipients is null || skipDomains.Count == 0)
        {
            return false;
        }

        foreach (var recipient in recipients)
        {
            var at = recipient.IndexOf('@');
            if (at < 0 || at == recipient.Length - 1)
            {
                continue;
            }
            var domain = recipient[(at + 1)..].ToLowerInvariant();
            if (skipDomains.Contains(domain))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ProtectAttachmentRequest(
        Guid TenantId,
        string? SenderEmail,
        IReadOnlyList<string>? Recipients,
        string AttachmentName,
        long AttachmentSizeBytes);

    private sealed record ProtectAttachmentResponse(string Status, string? ProtectedFileId);

    private sealed record OutlookAddInStatusResponse(
        bool Enabled,
        bool AutoEncryptOutgoingAttachments,
        int MinAttachmentSizeKb,
        int LifetimeProtectedCount);

    private sealed record ErrorResponse(string ReasonCode);
}
