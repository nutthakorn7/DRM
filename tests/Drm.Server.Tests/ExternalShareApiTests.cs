using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Drm.Server.Tests;

public sealed class ExternalShareApiTests : IDisposable
{
    private const string ClientApiKey = "secret-client-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-external-share-{Guid.NewGuid():N}.db");
    private readonly RecordingExternalShareVerificationSender verificationSender = new();
    private readonly WebApplicationFactory<Program> factory;

    public ExternalShareApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:ClientApiKey", ClientApiKey);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IExternalShareVerificationSender>();
                    services.AddSingleton<IExternalShareVerificationSender>(verificationSender);
                });
            });
    }

    [Fact]
    public async Task Guest_can_redeem_external_share_link_without_client_api_key()
    {
        using var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        using var guestClient = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        using var register = await RegisterFileAsync(setupClient, tenantId, fileId, Guid.NewGuid(), contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        using var create = await CreateShareLinkAsync(
            setupClient,
            tenantId,
            fileId,
            adminUserId,
            "Guest.User@Example.COM",
            DateTimeOffset.UtcNow.AddMinutes(20),
            maxUses: 2);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareLink = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        using var redeem = await RedeemShareLinkAsync(
            guestClient,
            tenantId,
            shareLink!.AccessToken,
            "guest.user@example.com");

        redeem.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await redeem.Content.ReadAsStringAsync();
        responseJson.Should().NotContain(shareLink.AccessToken);
        responseJson.ToLowerInvariant().Should().NotContain("tokenhash");
        responseJson.ToLowerInvariant().Should().NotContain("wrappedkey");
        responseJson.ToLowerInvariant().Should().NotContain("ciphertext");

        var redeemed = await redeem.Content.ReadFromJsonAsync<ExternalShareRedemptionResponse>();
        redeemed.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            ShareLinkId = shareLink.ShareLinkId,
            FileId = fileId,
            GuestEmail = "guest.user@example.com",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            MaxUses = 2,
            UsedCount = 1,
            ReasonCode = "external_share_link_redeemed"
        });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedLink = await dbContext.ExternalShareLinks.AsNoTracking().SingleAsync();
        storedLink.UsedCount.Should().Be(1);
        var auditEvents = await dbContext.AuditEvents.AsNoTracking().ToListAsync();
        auditEvents.Should().Contain(audit =>
            audit.EventType == "external_share_accessed" &&
            audit.ReasonCode == "external_share_link_redeemed" &&
            audit.TenantId == tenantId &&
            audit.FileId == fileId &&
            audit.UserId == null);
    }

    [Fact]
    public async Task Guest_redeem_returns_not_found_for_wrong_token_or_guest_email()
    {
        using var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        using var guestClient = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(setupClient, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        using var create = await CreateShareLinkAsync(
            setupClient,
            tenantId,
            fileId,
            Guid.NewGuid(),
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(20),
            maxUses: 2);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareLink = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        using var wrongToken = await RedeemShareLinkAsync(
            guestClient,
            tenantId,
            "not-the-token",
            "guest@example.com");
        using var wrongEmail = await RedeemShareLinkAsync(
            guestClient,
            tenantId,
            shareLink!.AccessToken,
            "other@example.com");

        wrongToken.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongEmail.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedLink = await dbContext.ExternalShareLinks.AsNoTracking().SingleAsync();
        storedLink.UsedCount.Should().Be(0);
    }

    [Fact]
    public async Task Guest_redeem_rejects_inactive_share_or_file_without_incrementing_use_count()
    {
        await AssertInactiveRedemptionAsync(
            mutateState: async (client, tenantId, fileId, shareLinkId) =>
            {
                using var revoke = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/share-links/{shareLinkId}/revoke", new
                {
                    tenantId,
                    adminUserId = Guid.NewGuid()
                });
                revoke.StatusCode.Should().Be(HttpStatusCode.OK);
            },
            expectedReasonCode: "share_link_revoked");

        await AssertInactiveRedemptionAsync(
            mutateState: async (_, _, _, shareLinkId) =>
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var link = await dbContext.ExternalShareLinks.SingleAsync(candidate => candidate.ShareLinkId == shareLinkId);
                link.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                await dbContext.SaveChangesAsync();
            },
            expectedReasonCode: "share_link_expired");

        await AssertInactiveRedemptionAsync(
            mutateState: async (client, tenantId, _, shareLinkId) =>
            {
                using var created = await RedeemShareLinkAsync(
                    client,
                    tenantId,
                    await GetAccessTokenAsync(shareLinkId),
                    "guest@example.com");
                created.StatusCode.Should().Be(HttpStatusCode.OK);
            },
            expectedReasonCode: "share_link_max_uses_exceeded",
            maxUses: 1,
            expectedUsedCountAfterFailure: 1);

        await AssertInactiveRedemptionAsync(
            mutateState: async (client, tenantId, fileId, _) =>
            {
                using var revoke = await client.PostAsync($"/api/files/{fileId}/revoke?tenantId={tenantId}", content: null);
                revoke.StatusCode.Should().Be(HttpStatusCode.OK);
            },
            expectedReasonCode: "file_revoked");

        await AssertInactiveRedemptionAsync(
            mutateState: async (_, tenantId, fileId, _) =>
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var file = await dbContext.ProtectedFiles.SingleAsync(candidate => candidate.TenantId == tenantId && candidate.Id == fileId);
                file.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                await dbContext.SaveChangesAsync();
            },
            expectedReasonCode: "file_expired");
    }

    [Fact]
    public async Task Guest_can_start_and_confirm_external_share_verification_without_client_api_key()
    {
        using var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        using var guestClient = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(setupClient, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        using var create = await CreateShareLinkAsync(
            setupClient,
            tenantId,
            fileId,
            Guid.NewGuid(),
            "Guest.User@Example.COM",
            DateTimeOffset.UtcNow.AddMinutes(20),
            maxUses: 2);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareLink = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        using var start = await StartVerificationAsync(
            guestClient,
            tenantId,
            shareLink!.AccessToken,
            "guest.user@example.com");

        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var startJson = await start.Content.ReadAsStringAsync();
        startJson.ToLowerInvariant().Should().NotContain("codehash");
        var started = await start.Content.ReadFromJsonAsync<ExternalShareVerificationStartResponse>();
        started.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            ShareLinkId = shareLink.ShareLinkId,
            GuestEmail = "guest.user@example.com",
            ReasonCode = "verification_code_sent"
        });
        started!.VerificationId.Should().NotBe(Guid.Empty);

        verificationSender.Messages.Should().ContainSingle(message =>
            message.TenantId == tenantId &&
            message.ShareLinkId == shareLink.ShareLinkId &&
            message.VerificationId == started.VerificationId &&
            message.GuestEmail == "guest.user@example.com");
        var deliveredCode = verificationSender.Messages[0].Code;
        deliveredCode.Should().MatchRegex("^\\d{6}$");
        startJson.Should().NotContain(deliveredCode);

        using var confirm = await ConfirmVerificationAsync(
            guestClient,
            tenantId,
            started.VerificationId,
            deliveredCode);

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmJson = await confirm.Content.ReadAsStringAsync();
        confirmJson.Should().NotContain(deliveredCode);
        confirmJson.ToLowerInvariant().Should().NotContain("codehash");
        var confirmed = await confirm.Content.ReadFromJsonAsync<ExternalShareVerificationConfirmResponse>();
        confirmed.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            VerificationId = started.VerificationId,
            ShareLinkId = shareLink.ShareLinkId,
            GuestEmail = "guest.user@example.com",
            ReasonCode = "verification_confirmed"
        });
        confirmed!.VerificationSessionToken.Should().NotBeNullOrWhiteSpace();
        confirmed.VerificationSessionToken.Length.Should().BeGreaterThan(30);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.ExternalShareVerifications.AsNoTracking().SingleAsync();
        stored.CodeHash.Should().NotBe(deliveredCode);
        stored.VerifiedAtUtc.Should().NotBeNull();
        stored.SessionTokenHash.Should().NotBe(confirmed.VerificationSessionToken);
        stored.SessionTokenHash.Should().NotBeNullOrWhiteSpace();
        stored.SessionExpiresAtUtc.Should().NotBeNull();
        var auditEvents = await dbContext.AuditEvents.AsNoTracking().ToListAsync();
        auditEvents.Should().Contain(audit =>
            audit.EventType == "external_share_verification" &&
            audit.ReasonCode == "verification_code_sent" &&
            audit.FileId == fileId);
        auditEvents.Should().Contain(audit =>
            audit.EventType == "external_share_verification" &&
            audit.ReasonCode == "verification_confirmed" &&
            audit.FileId == fileId);
    }

    [Fact]
    public async Task Guest_verification_start_returns_not_found_for_wrong_token_or_guest_email_without_sending_code()
    {
        using var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        using var guestClient = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(setupClient, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        using var create = await CreateShareLinkAsync(
            setupClient,
            tenantId,
            fileId,
            Guid.NewGuid(),
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(20),
            maxUses: 2);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareLink = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        using var wrongToken = await StartVerificationAsync(
            guestClient,
            tenantId,
            "wrong-token",
            "guest@example.com");
        using var wrongEmail = await StartVerificationAsync(
            guestClient,
            tenantId,
            shareLink!.AccessToken,
            "other@example.com");

        wrongToken.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongEmail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        verificationSender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Guest_verification_confirm_tracks_wrong_code_expiry_and_attempt_limit()
    {
        var first = await CreateStartedVerificationAsync();

        using var wrongCode = await ConfirmVerificationAsync(
            first.GuestClient,
            first.TenantId,
            first.VerificationId,
            DifferentCode(first.Code));
        wrongCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongCode.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("invalid_verification_code"));

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await dbContext.ExternalShareVerifications.SingleAsync(candidate => candidate.VerificationId == first.VerificationId);
            stored.AttemptCount.Should().Be(1);
            stored.SessionTokenHash.Should().BeNull();
        }

        var expired = await CreateStartedVerificationAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await dbContext.ExternalShareVerifications.SingleAsync(candidate => candidate.VerificationId == expired.VerificationId);
            stored.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        using var expiredConfirm = await ConfirmVerificationAsync(
            expired.GuestClient,
            expired.TenantId,
            expired.VerificationId,
            expired.Code);
        expiredConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await expiredConfirm.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("verification_expired"));

        var exhausted = await CreateStartedVerificationAsync();
        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            using var failed = await ConfirmVerificationAsync(
                exhausted.GuestClient,
                exhausted.TenantId,
                exhausted.VerificationId,
                DifferentCode(exhausted.Code));
            failed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using var blocked = await ConfirmVerificationAsync(
            exhausted.GuestClient,
            exhausted.TenantId,
            exhausted.VerificationId,
            exhausted.Code);
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("verification_attempts_exceeded"));
    }

    [Fact]
    public async Task Guest_verification_confirm_rechecks_share_and_file_state()
    {
        var revokedLink = await CreateStartedVerificationAsync();
        using var revokeLink = await revokedLink.SetupClient.PostAsJsonAsync(
            $"/api/admin/files/{revokedLink.FileId}/share-links/{revokedLink.ShareLinkId}/revoke",
            new
            {
                tenantId = revokedLink.TenantId,
                adminUserId = Guid.NewGuid()
            });
        revokeLink.StatusCode.Should().Be(HttpStatusCode.OK);

        using var linkConfirm = await ConfirmVerificationAsync(
            revokedLink.GuestClient,
            revokedLink.TenantId,
            revokedLink.VerificationId,
            revokedLink.Code);
        linkConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await linkConfirm.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("share_link_revoked"));

        var revokedFile = await CreateStartedVerificationAsync();
        using var revokeFile = await revokedFile.SetupClient.PostAsync(
            $"/api/files/{revokedFile.FileId}/revoke?tenantId={revokedFile.TenantId}",
            content: null);
        revokeFile.StatusCode.Should().Be(HttpStatusCode.OK);

        using var fileConfirm = await ConfirmVerificationAsync(
            revokedFile.GuestClient,
            revokedFile.TenantId,
            revokedFile.VerificationId,
            revokedFile.Code);
        fileConfirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fileConfirm.Content.ReadFromJsonAsync<ErrorResponse>())
            .Should().BeEquivalentTo(new ErrorResponse("file_revoked"));
    }

    [Fact]
    public async Task Guest_can_open_verified_viewer_session_without_key_or_content_release()
    {
        var started = await CreateStartedVerificationAsync();
        using var confirm = await ConfirmVerificationAsync(
            started.GuestClient,
            started.TenantId,
            started.VerificationId,
            started.Code);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await confirm.Content.ReadFromJsonAsync<ExternalShareVerificationConfirmResponse>();

        using var open = await OpenViewerSessionAsync(
            started.GuestClient,
            started.TenantId,
            confirmed!.VerificationSessionToken);

        open.StatusCode.Should().Be(HttpStatusCode.OK);
        var openJson = await open.Content.ReadAsStringAsync();
        openJson.Should().NotContain(confirmed.VerificationSessionToken);
        openJson.ToLowerInvariant().Should().NotContain("tokenhash");
        openJson.ToLowerInvariant().Should().NotContain("sessiontokenhash");
        openJson.ToLowerInvariant().Should().NotContain("wrappedkey");
        openJson.ToLowerInvariant().Should().NotContain("ciphertext");
        openJson.ToLowerInvariant().Should().NotContain("decrypted");

        var viewerSession = await open.Content.ReadFromJsonAsync<ExternalShareViewerSessionResponse>();
        viewerSession.Should().BeEquivalentTo(new
        {
            TenantId = started.TenantId,
            ShareLinkId = started.ShareLinkId,
            FileId = started.FileId,
            GuestEmail = "guest@example.com",
            ContentType = "application/pdf",
            WatermarkTemplate = "user:{userId}",
            DownloadDisabled = true,
            PrintDisabled = true,
            ExportDisabled = true,
            ReasonCode = "viewer_session_ready"
        });

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await dbContext.ExternalShareLinks.AsNoTracking().SingleAsync(candidate => candidate.ShareLinkId == started.ShareLinkId);
            link.UsedCount.Should().Be(1);
            var verification = await dbContext.ExternalShareVerifications.AsNoTracking().SingleAsync(candidate => candidate.VerificationId == started.VerificationId);
            verification.ViewerOpenedAtUtc.Should().NotBeNull();
            var auditEvents = await dbContext.AuditEvents.AsNoTracking().ToListAsync();
            auditEvents.Should().Contain(audit =>
                audit.EventType == "external_share_viewer" &&
                audit.ReasonCode == "external_share_viewer_opened" &&
                audit.FileId == started.FileId);
        }

        using var repeatOpen = await OpenViewerSessionAsync(
            started.GuestClient,
            started.TenantId,
            confirmed.VerificationSessionToken);

        repeatOpen.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await dbContext.ExternalShareLinks.AsNoTracking().SingleAsync(candidate => candidate.ShareLinkId == started.ShareLinkId);
            link.UsedCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Guest_viewer_session_rejects_invalid_or_inactive_verified_session()
    {
        var invalid = await CreateConfirmedVerificationAsync();
        using var invalidToken = await OpenViewerSessionAsync(
            invalid.Started.GuestClient,
            invalid.Started.TenantId,
            "wrong-session-token");
        invalidToken.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AssertViewerSessionRejectedAsync(
            mutateState: async started =>
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var verification = await dbContext.ExternalShareVerifications.SingleAsync(candidate => candidate.VerificationId == started.VerificationId);
                verification.SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                await dbContext.SaveChangesAsync();
            },
            expectedReasonCode: "verification_session_expired");

        await AssertViewerSessionRejectedAsync(
            mutateState: async started =>
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var link = await dbContext.ExternalShareLinks.SingleAsync(candidate => candidate.ShareLinkId == started.ShareLinkId);
                link.UsedCount = link.MaxUses;
                await dbContext.SaveChangesAsync();
            },
            expectedReasonCode: "share_link_max_uses_exceeded");

        await AssertViewerSessionRejectedAsync(
            mutateState: async started =>
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var link = await dbContext.ExternalShareLinks.SingleAsync(candidate => candidate.ShareLinkId == started.ShareLinkId);
                link.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                await dbContext.SaveChangesAsync();
            },
            expectedReasonCode: "share_link_expired");

        await AssertViewerSessionRejectedAsync(
            mutateState: async started =>
            {
                using var revokeLink = await started.SetupClient.PostAsJsonAsync(
                    $"/api/admin/files/{started.FileId}/share-links/{started.ShareLinkId}/revoke",
                    new
                    {
                        tenantId = started.TenantId,
                        adminUserId = Guid.NewGuid()
                    });
                revokeLink.StatusCode.Should().Be(HttpStatusCode.OK);
            },
            expectedReasonCode: "share_link_revoked");

        await AssertViewerSessionRejectedAsync(
            mutateState: async started =>
            {
                using var revokeFile = await started.SetupClient.PostAsync(
                    $"/api/files/{started.FileId}/revoke?tenantId={started.TenantId}",
                    content: null);
                revokeFile.StatusCode.Should().Be(HttpStatusCode.OK);
            },
            expectedReasonCode: "file_revoked");

        await AssertViewerSessionRejectedAsync(
            mutateState: async started =>
            {
                using var scope = factory.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var file = await dbContext.ProtectedFiles.SingleAsync(candidate => candidate.TenantId == started.TenantId && candidate.Id == started.FileId);
                file.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                await dbContext.SaveChangesAsync();
            },
            expectedReasonCode: "file_expired");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private async Task AssertInactiveRedemptionAsync(
        Func<HttpClient, Guid, Guid, Guid, Task> mutateState,
        string expectedReasonCode,
        int maxUses = 2,
        int expectedUsedCountAfterFailure = 0)
    {
        using var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        using var guestClient = factory.CreateClient();
        guestClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(setupClient, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        using var create = await CreateShareLinkAsync(
            setupClient,
            tenantId,
            fileId,
            Guid.NewGuid(),
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(20),
            maxUses);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareLink = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        await StoreAccessTokenAsync(shareLink!.ShareLinkId, shareLink.AccessToken);
        await mutateState(setupClient, tenantId, fileId, shareLink.ShareLinkId);

        using var redeem = await RedeemShareLinkAsync(
            guestClient,
            tenantId,
            shareLink.AccessToken,
            "guest@example.com");

        redeem.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await redeem.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse(expectedReasonCode));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedLink = await dbContext.ExternalShareLinks.AsNoTracking().SingleAsync(candidate => candidate.ShareLinkId == shareLink.ShareLinkId);
        storedLink.UsedCount.Should().Be(expectedUsedCountAfterFailure);
    }

    private static Task<HttpResponseMessage> RegisterFileAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType = "application/pdf")
    {
        return client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType,
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "user:{userId}"
        });
    }

    private static Task<HttpResponseMessage> CreateShareLinkAsync(
        HttpClient client,
        Guid tenantId,
        Guid fileId,
        Guid adminUserId,
        string guestEmail,
        DateTimeOffset expiresAtUtc,
        int maxUses)
    {
        return client.PostAsJsonAsync($"/api/admin/files/{fileId}/share-links", new
        {
            tenantId,
            adminUserId,
            guestEmail,
            expiresAtUtc,
            maxUses
        });
    }

    private static Task<HttpResponseMessage> RedeemShareLinkAsync(
        HttpClient client,
        Guid tenantId,
        string accessToken,
        string guestEmail)
    {
        return client.PostAsJsonAsync("/api/share-links/redeem", new
        {
            tenantId,
            accessToken,
            guestEmail
        });
    }

    private static Task<HttpResponseMessage> StartVerificationAsync(
        HttpClient client,
        Guid tenantId,
        string accessToken,
        string guestEmail)
    {
        return client.PostAsJsonAsync("/api/share-links/verification/start", new
        {
            tenantId,
            accessToken,
            guestEmail
        });
    }

    private static Task<HttpResponseMessage> ConfirmVerificationAsync(
        HttpClient client,
        Guid tenantId,
        Guid verificationId,
        string code)
    {
        return client.PostAsJsonAsync("/api/share-links/verification/confirm", new
        {
            tenantId,
            verificationId,
            code
        });
    }

    private static Task<HttpResponseMessage> OpenViewerSessionAsync(
        HttpClient client,
        Guid tenantId,
        string verificationSessionToken)
    {
        return client.PostAsJsonAsync("/api/share-links/viewer/session", new
        {
            tenantId,
            verificationSessionToken
        });
    }

    private async Task<ConfirmedVerification> CreateConfirmedVerificationAsync()
    {
        var started = await CreateStartedVerificationAsync();
        using var confirm = await ConfirmVerificationAsync(
            started.GuestClient,
            started.TenantId,
            started.VerificationId,
            started.Code);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await confirm.Content.ReadFromJsonAsync<ExternalShareVerificationConfirmResponse>();
        return new ConfirmedVerification(started, confirmed!.VerificationSessionToken);
    }

    private async Task AssertViewerSessionRejectedAsync(
        Func<StartedVerification, Task> mutateState,
        string expectedReasonCode)
    {
        var confirmed = await CreateConfirmedVerificationAsync();
        await mutateState(confirmed.Started);

        using var open = await OpenViewerSessionAsync(
            confirmed.Started.GuestClient,
            confirmed.Started.TenantId,
            confirmed.VerificationSessionToken);

        open.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await open.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new ErrorResponse(expectedReasonCode));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verification = await dbContext.ExternalShareVerifications.AsNoTracking().SingleAsync(candidate => candidate.VerificationId == confirmed.Started.VerificationId);
        verification.ViewerOpenedAtUtc.Should().BeNull();
    }

    private async Task<StartedVerification> CreateStartedVerificationAsync()
    {
        var setupClient = factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);
        var guestClient = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var register = await RegisterFileAsync(setupClient, tenantId, fileId, Guid.NewGuid());
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        using var create = await CreateShareLinkAsync(
            setupClient,
            tenantId,
            fileId,
            Guid.NewGuid(),
            "guest@example.com",
            DateTimeOffset.UtcNow.AddMinutes(20),
            maxUses: 2);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareLink = await create.Content.ReadFromJsonAsync<CreateExternalShareLinkResponse>();

        var previousMessageCount = verificationSender.Messages.Count;
        using var start = await StartVerificationAsync(guestClient, tenantId, shareLink!.AccessToken, "guest@example.com");
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await start.Content.ReadFromJsonAsync<ExternalShareVerificationStartResponse>();
        verificationSender.Messages.Count.Should().Be(previousMessageCount + 1);
        var message = verificationSender.Messages[^1];

        return new StartedVerification(
            setupClient,
            guestClient,
            tenantId,
            fileId,
            shareLink.ShareLinkId,
            started!.VerificationId,
            message.Code);
    }

    private static string DifferentCode(string code)
    {
        return code == "000000" ? "111111" : "000000";
    }

    private readonly Dictionary<Guid, string> accessTokensByShareLinkId = [];

    private Task StoreAccessTokenAsync(Guid shareLinkId, string accessToken)
    {
        accessTokensByShareLinkId[shareLinkId] = accessToken;
        return Task.CompletedTask;
    }

    private Task<string> GetAccessTokenAsync(Guid shareLinkId)
    {
        return Task.FromResult(accessTokensByShareLinkId[shareLinkId]);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

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
        string AccessToken);

    private sealed record ExternalShareRedemptionResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        string ReasonCode);

    private sealed record ExternalShareVerificationStartResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid VerificationId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        string ReasonCode);

    private sealed record ExternalShareVerificationConfirmResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid VerificationId,
        string GuestEmail,
        DateTimeOffset SessionExpiresAtUtc,
        string VerificationSessionToken,
        string ReasonCode);

    private sealed record ExternalShareViewerSessionResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        string ContentType,
        DateTimeOffset FileExpiresAtUtc,
        DateTimeOffset ShareLinkExpiresAtUtc,
        DateTimeOffset SessionExpiresAtUtc,
        string WatermarkTemplate,
        bool DownloadDisabled,
        bool PrintDisabled,
        bool ExportDisabled,
        string ReasonCode);

    private sealed record ConfirmedVerification(
        StartedVerification Started,
        string VerificationSessionToken);

    private sealed record StartedVerification(
        HttpClient SetupClient,
        HttpClient GuestClient,
        Guid TenantId,
        Guid FileId,
        Guid ShareLinkId,
        Guid VerificationId,
        string Code);

    private sealed record ErrorResponse(string ReasonCode);

    private sealed class RecordingExternalShareVerificationSender : IExternalShareVerificationSender
    {
        private readonly List<ExternalShareVerificationMessage> messages = [];

        public IReadOnlyList<ExternalShareVerificationMessage> Messages
        {
            get
            {
                lock (messages)
                {
                    return messages.ToList();
                }
            }
        }

        public Task SendAsync(ExternalShareVerificationMessage message, CancellationToken cancellationToken)
        {
            lock (messages)
            {
                messages.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
