using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminFolderWatcherApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-fw-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminFolderWatcherApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_save_and_retrieve_folder_watcher_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/folder-watcher/config", new
        {
            tenantId,
            watchedFolders = new[]
            {
                new { path = @"C:\Shares\Confidential", policyTemplateId = (Guid?)null },
                new { path = @"D:\Engineering\Specs", policyTemplateId = (Guid?)null }
            },
            enabled = true
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        var loaded = await client.GetFromJsonAsync<FolderWatcherConfigResponse>(
            $"/api/admin/folder-watcher/config?tenantId={tenantId}");
        loaded.Should().NotBeNull();
        loaded!.Enabled.Should().BeTrue();
        loaded.WatchedFolders.Should().HaveCount(2);
        loaded.WatchedFolders[0].Path.Should().Be(@"C:\Shares\Confidential");
    }

    [Fact]
    public async Task Get_returns_404_when_no_config()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/admin/folder-watcher/config?tenantId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Service_report_updates_status_fields()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/folder-watcher/config", new
        {
            tenantId,
            watchedFolders = Array.Empty<object>(),
            enabled = true
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        using var report = await client.PostAsJsonAsync("/api/admin/folder-watcher/report", new
        {
            tenantId,
            hostname = "FS-01",
            status = "ok",
            filesProtected = 42
        });
        report.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loaded = await client.GetFromJsonAsync<FolderWatcherConfigResponse>(
            $"/api/admin/folder-watcher/config?tenantId={tenantId}");
        loaded!.Hostname.Should().Be("FS-01");
        loaded.LastReportStatus.Should().Be("ok");
        loaded.LastFilesProtected.Should().Be(42);
    }

    [Fact]
    public async Task Service_event_writes_audit_row_and_appears_in_listing()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var post = await client.PostAsJsonAsync("/api/admin/folder-watcher/events", new
        {
            tenantId,
            hostname = "FS-01",
            folderPath = @"C:\Shares\Confidential",
            fileName = "report.docx",
            fileSize = 9876L,
            status = "protected",
            fileId
        });
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var events = await client.GetFromJsonAsync<List<FolderWatcherEventResponse>>(
            $"/api/admin/folder-watcher/events?tenantId={tenantId}");
        events.Should().NotBeNull();
        events!.Should().ContainSingle();
        events[0].FileName.Should().Be("report.docx");
        events[0].Status.Should().Be("protected");
        events[0].FileId.Should().Be(fileId);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record FolderWatcherConfigResponse(
        Guid TenantId,
        IReadOnlyList<WatchedFolder> WatchedFolders,
        bool Enabled,
        string? LastReportStatus,
        DateTimeOffset? LastReportAtUtc,
        int LastFilesProtected,
        string? Hostname,
        DateTimeOffset UpdatedAtUtc);

    private sealed record WatchedFolder(string Path, Guid? PolicyTemplateId);

    private sealed record FolderWatcherEventResponse(
        long Id,
        string Hostname,
        string FolderPath,
        string FileName,
        long FileSize,
        string Status,
        Guid? FileId,
        DateTimeOffset OccurredAtUtc);
}
