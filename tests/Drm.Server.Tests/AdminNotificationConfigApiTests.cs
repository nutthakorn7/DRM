using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminNotificationConfigApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-notif-cfg-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminNotificationConfigApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_save_and_retrieve_notification_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var put = await client.PutAsJsonAsync("/api/admin/notification-config", new
        {
            tenantId,
            adminEmailsCsv = "admin@corp.com,security@corp.com",
            notifyOnExternalShareViewed = true,
            notifyOnFileRevoked = true,
            notifyOnAccessDenied = false,
            notifyOnShareLinkCreated = true
        });

        put.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await client.GetFromJsonAsync<NotificationConfigResponse>(
            $"/api/admin/notification-config?tenantId={tenantId}");

        config.Should().NotBeNull();
        config!.AdminEmailsCsv.Should().Be("admin@corp.com,security@corp.com");
        config.NotifyOnExternalShareViewed.Should().BeTrue();
        config.NotifyOnFileRevoked.Should().BeTrue();
        config.NotifyOnAccessDenied.Should().BeFalse();
        config.NotifyOnShareLinkCreated.Should().BeTrue();
    }

    [Fact]
    public async Task Get_notification_config_returns_not_found_when_unconfigured()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/admin/notification-config?tenantId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_update_existing_notification_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var first = await client.PutAsJsonAsync("/api/admin/notification-config", new
        {
            tenantId,
            adminEmailsCsv = "old@corp.com",
            notifyOnExternalShareViewed = false,
            notifyOnFileRevoked = false,
            notifyOnAccessDenied = false,
            notifyOnShareLinkCreated = false
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PutAsJsonAsync("/api/admin/notification-config", new
        {
            tenantId,
            adminEmailsCsv = "new@corp.com",
            notifyOnExternalShareViewed = true,
            notifyOnFileRevoked = true,
            notifyOnAccessDenied = false,
            notifyOnShareLinkCreated = false
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var config = await client.GetFromJsonAsync<NotificationConfigResponse>(
            $"/api/admin/notification-config?tenantId={tenantId}");

        config!.AdminEmailsCsv.Should().Be("new@corp.com");
        config.NotifyOnExternalShareViewed.Should().BeTrue();
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var f in new[] { path, $"{path}-wal", $"{path}-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private sealed record NotificationConfigResponse(
        Guid TenantId,
        string AdminEmailsCsv,
        bool NotifyOnExternalShareViewed,
        bool NotifyOnFileRevoked,
        bool NotifyOnAccessDenied,
        bool NotifyOnShareLinkCreated,
        DateTimeOffset UpdatedAtUtc);
}
