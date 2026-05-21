using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class MeSharesEndpointsTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-meshares-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public MeSharesEndpointsTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                b.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Lists_shares_for_caller_with_recipient_and_expiry()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var fileId = await SeedQuickShareAsync(client, tenantId, userId, recipient: "alice@example.com");

        using var listResp = await client.GetAsync(
            $"/api/me/shares?tenantId={tenantId}&userId={userId}");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await listResp.Content.ReadFromJsonAsync<MySharesResponse>();

        payload.Should().NotBeNull();
        payload!.Shares.Should().HaveCount(1);
        var row = payload.Shares.Single();
        row.FileId.Should().Be(fileId);
        row.GuestEmail.Should().Be("alice@example.com");
        row.ShareRevoked.Should().BeFalse();
        row.FileRevoked.Should().BeFalse();
        row.UsedCount.Should().Be(0);
        row.Permissions.Should().Contain("View");
    }

    [Fact]
    public async Task Only_returns_shares_for_the_requested_user()
    {
        // Privacy guard — a user's My Shares page must never leak rows
        // owned by other users in the same tenant.
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await SeedQuickShareAsync(client, tenantId, alice, recipient: "carol@example.com");
        await SeedQuickShareAsync(client, tenantId, bob, recipient: "dave@example.com");

        using var aliceResp = await client.GetAsync(
            $"/api/me/shares?tenantId={tenantId}&userId={alice}");
        var alicePayload = await aliceResp.Content.ReadFromJsonAsync<MySharesResponse>();
        alicePayload!.Shares.Should().ContainSingle();
        alicePayload.Shares.Single().GuestEmail.Should().Be("carol@example.com");
    }

    [Fact]
    public async Task Returns_shares_sorted_newest_first()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedQuickShareAsync(client, tenantId, userId, recipient: "first@example.com");
        await Task.Delay(50);
        await SeedQuickShareAsync(client, tenantId, userId, recipient: "second@example.com");
        await Task.Delay(50);
        await SeedQuickShareAsync(client, tenantId, userId, recipient: "third@example.com");

        using var listResp = await client.GetAsync(
            $"/api/me/shares?tenantId={tenantId}&userId={userId}");
        var payload = await listResp.Content.ReadFromJsonAsync<MySharesResponse>();

        payload!.Shares.Select(s => s.GuestEmail).Should().Equal(
            "third@example.com", "second@example.com", "first@example.com");
    }

    [Fact]
    public async Task Rejects_empty_identifiers()
    {
        using var client = factory.CreateClient();
        using var resp = await client.GetAsync($"/api/me/shares?tenantId={Guid.Empty}&userId={Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_empty_list_when_user_has_no_shares_yet()
    {
        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/me/shares?tenantId={Guid.NewGuid()}&userId={Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await resp.Content.ReadFromJsonAsync<MySharesResponse>();
        payload!.Shares.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_revoke_flips_revoked_flag_for_owner_of_underlying_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var (fileId, shareLinkId) = await SeedQuickShareAndGetIdsAsync(
            client, tenantId, userId, recipient: "alice@example.com");

        using var revokeResp = await client.PostAsJsonAsync(
            $"/api/me/shares/{shareLinkId}/revoke",
            new { tenantId, userId });
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var listResp = await client.GetAsync(
            $"/api/me/shares?tenantId={tenantId}&userId={userId}");
        var payload = await listResp.Content.ReadFromJsonAsync<MySharesResponse>();
        var row = payload!.Shares.Single();
        row.ShareRevoked.Should().BeTrue();
        row.RevokedAtUtc.Should().NotBeNull();
        row.RevocationReason.Should().Be("self_revoked",
            "revocation reason must distinguish self-revoke from admin revoke / brute-force auto-revoke in the audit trail");
    }

    [Fact]
    public async Task Self_revoke_rejects_share_belonging_to_another_user()
    {
        // Privacy + authorization guard — Alice must not be able to
        // revoke Bob's share even if she somehow learns the shareLinkId.
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        var (_, bobsShareLink) = await SeedQuickShareAndGetIdsAsync(
            client, tenantId, bob, recipient: "dave@example.com");

        using var aliceTry = await client.PostAsJsonAsync(
            $"/api/me/shares/{bobsShareLink}/revoke",
            new { tenantId, userId = alice });
        // 404 not 403 — we deliberately don't leak "share exists but isn't yours".
        aliceTry.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Bob's share must still be active.
        using var listResp = await client.GetAsync(
            $"/api/me/shares?tenantId={tenantId}&userId={bob}");
        var payload = await listResp.Content.ReadFromJsonAsync<MySharesResponse>();
        payload!.Shares.Single().ShareRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Self_revoke_is_idempotent_when_already_revoked()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (_, shareLinkId) = await SeedQuickShareAndGetIdsAsync(
            client, tenantId, userId, recipient: "alice@example.com");

        using var first  = await client.PostAsJsonAsync($"/api/me/shares/{shareLinkId}/revoke", new { tenantId, userId });
        using var second = await client.PostAsJsonAsync($"/api/me/shares/{shareLinkId}/revoke", new { tenantId, userId });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "a second revoke on an already-revoked share must succeed silently (no error toast on re-click)");
    }

    [Fact]
    public async Task Self_revoke_returns_404_for_unknown_share_link()
    {
        using var client = factory.CreateClient();
        using var resp = await client.PostAsJsonAsync(
            $"/api/me/shares/{Guid.NewGuid()}/revoke",
            new { tenantId = Guid.NewGuid(), userId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<(Guid FileId, Guid ShareLinkId)> SeedQuickShareAndGetIdsAsync(
        HttpClient client, Guid tenantId, Guid userId, string recipient)
    {
        var body = new
        {
            tenantId, userId,
            recipientEmail = recipient,
            fileName = $"{Guid.NewGuid():N}.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"payload-{recipient}")),
            expiresInHours = 168,
            allowPrint = false
        };
        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var seeded = await resp.Content.ReadFromJsonAsync<SeededShare>();
        return (seeded!.FileId, seeded.ShareLinkId);
    }

    private static async Task<Guid> SeedQuickShareAsync(HttpClient client, Guid tenantId, Guid userId, string recipient)
    {
        var body = new
        {
            tenantId,
            userId,
            recipientEmail = recipient,
            fileName = $"{Guid.NewGuid():N}.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"payload-{recipient}")),
            expiresInHours = 168,
            allowPrint = false
        };
        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await resp.Content.ReadFromJsonAsync<SeededShare>();
        return json!.FileId;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var p in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(p)) File.Delete(p);
        }
    }

    private sealed record SeededShare(Guid FileId, Guid ShareLinkId, string ShareUrl);
}

internal sealed record MySharesResponse(Guid TenantId, Guid UserId, IReadOnlyList<MyShareRow> Shares);

internal sealed record MyShareRow(
    Guid ShareLinkId,
    Guid FileId,
    string ContentType,
    string GuestEmail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaxUses,
    int UsedCount,
    bool ShareRevoked,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason,
    bool FileRevoked,
    string Permissions);
