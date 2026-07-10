using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class QuickShareEndpointsTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-quickshare-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public QuickShareEndpointsTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                b.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Quick_share_creates_file_grants_and_returns_share_url_in_one_call()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var body = new
        {
            tenantId, userId,
            recipientEmail = "bob@example.com",
            fileName = "pitch.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("PITCH DECK BYTES")),
            expiresInHours = 168,
            allowPrint = false
        };

        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();
        result.Should().NotBeNull();
        result!.FileId.Should().NotBe(Guid.Empty);
        // Share URL now mirrors AdminFilesEndpoints' BuildExternalShareUrl
        // shape so recipients land on a pre-filled /share/ form without
        // typing a tenant GUID. See SECURITY.md and the share UX commit.
        result.ShareUrl.Should().Contain("/share/?");
        result.ShareUrl.Should().Contain("tenantId=");
        result.ShareUrl.Should().Contain("accessToken=");
        result.ShareUrl.Should().Contain("guestEmail=");
        result.RecipientEmail.Should().Be("bob@example.com");
        result.Permissions.Should().Be("View");
        result.OriginalFileSizeBytes.Should().Be("PITCH DECK BYTES"u8.Length);
        result.ExpiresAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(168), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Quick_share_persists_the_original_filename_and_admin_console_shows_it()
    {
        // Regression guard for the "GUID-paste operational model" finding
        // (2026-07-01 UX audit): every protected file used to be addressed
        // only by its GUID everywhere in the product, including the admin
        // console's file list — the original filename was already sent on
        // this exact request and simply discarded. This proves it now
        // survives end-to-end: QuickShare -> stored -> shown by the same
        // list endpoint the admin console's Files panel calls.
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var resp = await client.PostAsJsonAsync("/api/me/share", new
        {
            tenantId,
            userId = Guid.NewGuid(),
            recipientEmail = "grace@example.com",
            fileName = "Q4-Sales-Contract.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("contract bytes")),
            expiresInHours = 24,
            allowPrint = false,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();

        using var listResp = await client.GetAsync($"/api/admin/files?tenantId={tenantId}");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var files = await listResp.Content.ReadFromJsonAsync<List<AdminFileRow>>();
        files.Should().ContainSingle(f => f.FileId == created!.FileId && f.FileName == "Q4-Sales-Contract.pdf");
    }

    private sealed record AdminFileRow(Guid FileId, string FileName);

    [Fact]
    public async Task Quick_share_url_uses_https_when_forwarded_proto_is_https()
    {
        // Behind Caddy (TLS terminator) the app sees internal http; the real
        // edge scheme rides in X-Forwarded-Proto. UseForwardedHeaders must make
        // the generated share URL https so recipients never get an http:// link.
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/share")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(), userId = Guid.NewGuid(),
                recipientEmail = "proto@example.com",
                fileName = "doc.pdf", contentType = "application/pdf",
                fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")),
                expiresInHours = 24, allowPrint = false,
            }),
        };
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();
        result!.ShareUrl.Should().StartWith("https://", "X-Forwarded-Proto: https must drive the generated scheme");
    }

    [Fact]
    public async Task Quick_share_with_allow_print_grants_view_and_print_permissions()
    {
        using var client = factory.CreateClient();
        var body = new
        {
            tenantId = Guid.NewGuid(), userId = Guid.NewGuid(),
            recipientEmail = "alice@example.com",
            fileName = "doc.pdf", contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")),
            expiresInHours = 24,
            allowPrint = true
        };
        var resp = await client.PostAsJsonAsync("/api/me/share", body);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();
        result!.Permissions.Should().Contain("Print");
    }

    [Fact]
    public async Task Quick_share_with_full_permission_picker_grants_every_chosen_flag()
    {
        // Stage 7 per-share permission picker: the agent right-click
        // flow now sends individual AllowCopy / AllowEdit /
        // AllowExportOriginal flags. Verify they all map onto the
        // Permission bitfield on the ProtectedFile.
        using var client = factory.CreateClient();
        var body = new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            recipientEmail = "carol@example.com",
            fileName = "spec.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("body")),
            expiresInHours = 168,
            allowPrint = true,
            allowCopy = true,
            allowEdit = true,
            allowExportOriginal = true
        };

        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();

        result.Should().NotBeNull();
        // Permission.ToString() emits the flags in declaration order,
        // comma-separated. We don't lock the exact serialisation —
        // just assert every chosen flag is present.
        result!.Permissions.Should().Contain("View");
        result.Permissions.Should().Contain("Print");
        result.Permissions.Should().Contain("Copy");
        result.Permissions.Should().Contain("Edit");
        result.Permissions.Should().Contain("ExportOriginal");
    }

    [Fact]
    public async Task Quick_share_picker_can_grant_copy_without_print_or_edit()
    {
        // Realistic mid-tier case: a finance team gets "View + Copy
        // text" so they can paste numbers into a spreadsheet, but
        // can't print or edit.
        using var client = factory.CreateClient();
        var body = new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            recipientEmail = "dave@example.com",
            fileName = "q3-revenue.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("body")),
            expiresInHours = 24,
            allowPrint = false,
            allowCopy = true,
            allowEdit = false,
            allowExportOriginal = false
        };

        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();

        result!.Permissions.Should().Contain("View");
        result.Permissions.Should().Contain("Copy");
        result.Permissions.Should().NotContain("Print");
        result.Permissions.Should().NotContain("Edit");
        result.Permissions.Should().NotContain("ExportOriginal");
    }

    [Fact]
    public async Task Quick_share_backwards_compat_works_without_the_new_fields()
    {
        // Existing /me/ web caller only sends AllowPrint — must keep
        // working without the new bool? fields.
        using var client = factory.CreateClient();
        var body = new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            recipientEmail = "eve@example.com",
            fileName = "doc.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")),
            expiresInHours = 24,
            allowPrint = false
            // intentionally no allowCopy / allowEdit / allowExportOriginal
        };

        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();

        result!.Permissions.Should().Be("View",
            "no extra perms means View only — same behaviour as before Stage 7");
    }

    [Fact]
    public async Task Quick_share_rejects_invalid_email()
    {
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/me/share", new
        {
            tenantId = Guid.NewGuid(), userId = Guid.NewGuid(),
            recipientEmail = "not-an-email",
            fileName = "doc.pdf", contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")),
            expiresInHours = 24, allowPrint = false
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Quick_share_rejects_zero_or_oversized_expires_in_hours()
    {
        using var client = factory.CreateClient();
        foreach (var hours in new[] { 0, -1, 1_000_000 })
        {
            var resp = await client.PostAsJsonAsync("/api/me/share", new
            {
                tenantId = Guid.NewGuid(), userId = Guid.NewGuid(),
                recipientEmail = "alice@example.com",
                fileName = "x.pdf", contentType = "application/pdf",
                fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")),
                expiresInHours = hours, allowPrint = false
            });
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Quick_share_accepts_container_content_type_with_full_picker()
    {
        // Stage 9 — folder per-recipient share. The agent's "Share with
        // recipient" button on the Container section calls /api/me/share
        // with the packed .drmcontainer as the payload and
        // contentType="application/vnd.zcrdrm.container". The endpoint
        // must accept this just like any other content type and produce
        // the same recipient binding + permissions + expiry + share URL
        // as a normal file Quick Send — confirms the agent's Container
        // section can reuse the file Quick Send wire shape with zero
        // server changes.
        using var client = factory.CreateClient();
        var body = new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            recipientEmail = "frank@example.com",
            fileName = "q3-deck.drmcontainer",
            contentType = "application/vnd.zcrdrm.container",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("PRETEND CONTAINER BYTES")),
            expiresInHours = 720,
            allowPrint = true,
            allowCopy = false,
            allowEdit = true,
            allowExportOriginal = false,
        };

        using var resp = await client.PostAsJsonAsync("/api/me/share", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();

        result.Should().NotBeNull();
        result!.ShareUrl.Should().Contain("/share/?");
        result.RecipientEmail.Should().Be("frank@example.com");
        result.Permissions.Should().Contain("View");
        result.Permissions.Should().Contain("Print");
        result.Permissions.Should().Contain("Edit");
        result.Permissions.Should().NotContain("Copy");
        result.Permissions.Should().NotContain("ExportOriginal");
        result.ExpiresAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(720), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Quick_share_token_is_verifiable_through_recipient_flow()
    {
        // Stage 12 regression guard. The QuickShare endpoint historically
        // hashed the access token with Convert.ToHexString(SHA-256) while
        // ExternalShareToken.Hash uses Convert.ToBase64String(SHA-256).
        // That meant /share/ → /api/share-links/verification/start
        // could NEVER find a QuickShare-created link → silent 404 → the
        // recipient never received a verification code → the whole
        // outbound demo flow broke. This test exercises Create-Share →
        // Hit-Verification-Start as a round trip so the two formats
        // can never drift again.
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // 1) Create a share via Quick Send (the same way the agent does).
        using var createResp = await client.PostAsJsonAsync("/api/me/share", new
        {
            tenantId,
            userId,
            recipientEmail = "round-trip@example.com",
            fileName = "doc.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")),
            expiresInHours = 24,
            allowPrint = false,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var share = await createResp.Content.ReadFromJsonAsync<QuickShareResponse>();
        share.Should().NotBeNull();

        // 2) Extract the raw token from the share URL (recipient receives
        //    this in the email).
        var token = System.Web.HttpUtility.ParseQueryString(
                new Uri(share!.ShareUrl).Query)["accessToken"];
        token.Should().NotBeNullOrWhiteSpace();

        // 3) Recipient hits /api/share-links/verification/start with the
        //    raw token + their email. Pre-Stage-12 this returned 404
        //    because the hash didn't match the stored TokenHash.
        using var verifResp = await client.PostAsJsonAsync(
            "/api/share-links/verification/start",
            new { tenantId, accessToken = token, guestEmail = "round-trip@example.com" });

        // Must be 200 OK with a verificationId — proving the share-link
        // lookup found the row the QuickShare endpoint wrote.
        verifResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "QuickShare token hash format must match what ExternalShareToken.Hash computes");
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record QuickShareResponse(
        Guid FileId, Guid ShareLinkId, string ShareUrl,
        DateTimeOffset ExpiresAtUtc, string RecipientEmail,
        string Permissions, int OriginalFileSizeBytes);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task QuickShare_with_mismatched_header_returns_400_tenant_mismatch()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/share")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                userId = Guid.NewGuid(),
                recipientEmail = "x@example.com",
                fileName = "x.pdf",
                contentType = "application/pdf",
                fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hi")),
                expiresInHours = 24,
                allowPrint = false,
            }),
        };
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<QuickShareErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record QuickShareErrorBody(string ReasonCode);
}
