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
        result.ShareUrl.Should().Contain("/share/?token=");
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
}
