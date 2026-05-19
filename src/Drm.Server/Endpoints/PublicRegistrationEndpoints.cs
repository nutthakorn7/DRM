using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class PublicRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapPublicRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/register", SubmitAsync);
        endpoints.MapGet("/api/register/verify", VerifyAsync);
        return endpoints;
    }

    private static async Task<IResult> SubmitAsync(
        RegistrationRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        RegistrationSettings settings,
        IRegistrationEmailSender emailSender,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.TenantName))
            return Results.BadRequest(new { reasonCode = "tenant_name_required" });
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
            return Results.BadRequest(new { reasonCode = "admin_email_required" });

        // Check for name collision in live tenants
        if (await dbContext.Tenants.AnyAsync(t => t.Name == request.TenantName, cancellationToken))
            return Results.Conflict(new { reasonCode = "tenant_name_taken" });

        // Check for existing active (not rejected) registration with same name
        if (await dbContext.TenantRegistrations.AnyAsync(
                r => r.TenantName == request.TenantName &&
                     r.Status != RegistrationStatus.Rejected, cancellationToken))
            return Results.Conflict(new { reasonCode = "registration_already_exists" });

        var token = RegistrationTokens.Generate();
        var tokenHash = RegistrationTokens.Hash(token);
        var registrationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var sendEmail = settings.RequireEmailVerification && emailSender is not NoopRegistrationEmailSender;

        var reg = new TenantRegistrationEntity
        {
            RegistrationId = registrationId,
            TenantName = request.TenantName.Trim(),
            DisplayName = (request.DisplayName ?? request.TenantName).Trim(),
            AdminEmail = request.AdminEmail.Trim().ToLowerInvariant(),
            AdminDisplayName = (request.AdminDisplayName ?? request.AdminEmail).Trim(),
            MaxEncrypters = request.MaxEncrypters,
            Status = RegistrationStatus.Pending,
            TokenHash = tokenHash,
            TokenExpiresAtUtc = now.AddHours(24),
            RequestedAtUtc = now
        };

        dbContext.TenantRegistrations.Add(reg);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (sendEmail)
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var verifyUrl = $"{baseUrl}/api/register/verify?token={Uri.EscapeDataString(token)}";
            try
            {
                await emailSender.SendVerificationAsync(reg.AdminEmail, verifyUrl, cancellationToken);
            }
            catch
            {
                // Email failed — still return 202 so registration was recorded
            }

            return Results.Accepted(null, new RegistrationResponse(
                registrationId, RegistrationStatus.Pending,
                "Verification email sent. Please check your inbox.",
                null, null));
        }

        // No email required — auto-verify
        return await AutoVerifyAsync(reg, dbContext, settings, cancellationToken);
    }

    private static async Task<IResult> VerifyAsync(
        string? token,
        AppDbContext dbContext,
        RegistrationSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(new { reasonCode = "token_required" });

        var tokenHash = RegistrationTokens.Hash(token);
        var reg = await dbContext.TenantRegistrations
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (reg is null)
            return Results.NotFound(new { reasonCode = "invalid_token" });

        if (reg.Status == RegistrationStatus.Approved)
            return Results.Ok(new RegistrationResponse(
                reg.RegistrationId, RegistrationStatus.Approved,
                "Already approved.", reg.CreatedTenantId, null));

        if (reg.Status == RegistrationStatus.Rejected)
            return Results.BadRequest(new { reasonCode = "registration_rejected" });

        if (reg.TokenExpiresAtUtc < DateTimeOffset.UtcNow)
            return Results.BadRequest(new { reasonCode = "token_expired" });

        return await AutoVerifyAsync(reg, dbContext, settings, cancellationToken);
    }

    private static async Task<IResult> AutoVerifyAsync(
        TenantRegistrationEntity reg,
        AppDbContext dbContext,
        RegistrationSettings settings,
        CancellationToken cancellationToken)
    {
        reg.Status = RegistrationStatus.Verified;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!settings.RequireAdminApproval)
            return await AutoApproveAsync(reg, dbContext, cancellationToken);

        return Results.Accepted(null, new RegistrationResponse(
            reg.RegistrationId, RegistrationStatus.Verified,
            "Email verified. Waiting for admin approval.",
            null, null));
    }

    internal static async Task<IResult> AutoApproveAsync(
        TenantRegistrationEntity reg,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Check tenant name still available
        if (await dbContext.Tenants.AnyAsync(t => t.Name == reg.TenantName, cancellationToken))
        {
            reg.Status = RegistrationStatus.Rejected;
            reg.ReviewNotes = "Tenant name taken by another tenant during approval.";
            reg.ReviewedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Conflict(new { reasonCode = "tenant_name_taken" });
        }

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        dbContext.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            Name = reg.TenantName,
            DisplayName = reg.DisplayName,
            Status = TenantStatus.Active,
            MaxEncrypters = reg.MaxEncrypters,
            CreatedAtUtc = now
        });

        dbContext.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = reg.AdminEmail,
            DisplayName = reg.AdminDisplayName,
            CreatedAtUtc = now
        });

        reg.Status = RegistrationStatus.Approved;
        reg.ReviewedAtUtc = now;
        reg.CreatedTenantId = tenantId;
        reg.CreatedUserId = userId;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/admin/tenants/{tenantId}",
            new RegistrationResponse(reg.RegistrationId, RegistrationStatus.Approved,
                "Registration approved. Tenant created.", tenantId, userId));
    }

    private sealed record RegistrationRequest(
        string TenantName,
        string? DisplayName,
        string AdminEmail,
        string? AdminDisplayName,
        int? MaxEncrypters);

    internal sealed record RegistrationResponse(
        Guid RegistrationId,
        RegistrationStatus Status,
        string Message,
        Guid? TenantId,
        Guid? UserId);
}
