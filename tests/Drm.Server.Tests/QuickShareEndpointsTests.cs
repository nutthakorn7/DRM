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
