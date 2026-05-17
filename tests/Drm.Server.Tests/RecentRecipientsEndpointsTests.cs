using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class RecentRecipientsEndpointsTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-recents-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public RecentRecipientsEndpointsTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                b.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Recent_recipients_returns_distinct_emails_owned_by_caller_ordered_by_most_recent()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        await ShareAsync(client, tenantId, userId, "alice@example.com");
        await ShareAsync(client, tenantId, userId, "alice@example.com");
        await ShareAsync(client, tenantId, userId, "bob@example.com");
        await ShareAsync(client, tenantId, otherUser, "carol@example.com");

        var recents = await client.GetFromJsonAsync<List<RecentRecipientDto>>(
            $"/api/me/recent-recipients?tenantId={tenantId}&userId={userId}");

        recents.Should().NotBeNull();
        recents!.Select(r => r.Email)
            .Should().BeEquivalentTo(new[] { "alice@example.com", "bob@example.com" });
        recents.Single(r => r.Email == "alice@example.com").UseCount.Should().Be(2);
    }

    [Fact]
    public async Task Recent_recipients_validates_required_identifiers()
    {
        using var client = factory.CreateClient();
        using var blank = await client.GetAsync($"/api/me/recent-recipients?tenantId={Guid.Empty}&userId={Guid.NewGuid()}");
        blank.IsSuccessStatusCode.Should().BeFalse();
    }

    private static async Task ShareAsync(HttpClient client, Guid tenantId, Guid userId, string recipient)
    {
        var body = new
        {
            tenantId, userId,
            recipientEmail = recipient,
            fileName = "x.pdf",
            contentType = "application/pdf",
            fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")),
            expiresInHours = 24,
            allowPrint = false
        };
        var resp = await client.PostAsJsonAsync("/api/me/share", body);
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record RecentRecipientDto(string Email, int UseCount, DateTimeOffset LastSentAtUtc);
}
