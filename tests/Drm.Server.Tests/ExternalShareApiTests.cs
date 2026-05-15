using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class ExternalShareApiTests : IDisposable
{
    private const string ClientApiKey = "secret-client-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-external-share-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public ExternalShareApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:ClientApiKey", ClientApiKey);
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

    private sealed record ErrorResponse(string ReasonCode);
}
