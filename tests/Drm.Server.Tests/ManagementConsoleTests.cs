using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ManagementConsoleTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-management-console-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public ManagementConsoleTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_path_redirects_to_console_root()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be("/admin/");
    }

    [Fact]
    public async Task Admin_console_index_is_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/admin/");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        html.Should().Contain("zcrDRM");
        html.Should().Contain("Admin credential");
        html.Should().Contain("Tenant ID");
        html.Should().Contain("rel=\"icon\"");
        html.Should().Contain("Groups");
        html.Should().Contain("Protected files");
        html.Should().Contain("Apply policy template");
        html.Should().Contain("Policy template ID");
        html.Should().Contain("Delete protected copy");
        html.Should().Contain("deleteCopyForm");
        html.Should().Contain("Target device ID");
        html.Should().Contain("Command status");
        html.Should().Contain("commandsBody");
        html.Should().Contain("Refresh commands");
        html.Should().Contain("External sharing");
        html.Should().Contain("createShareLinkForm");
        html.Should().Contain("shareLinksBody");
        html.Should().Contain("Refresh share links");
        html.Should().Contain("Policy templates");
        html.Should().Contain("Policy simulator");
        html.Should().Contain("Simulate access");
        html.Should().Contain("Agent devices");
        html.Should().Contain("Device health");
        html.Should().Contain("deviceHealthSummary");
        html.Should().Contain("Stale after");
        html.Should().Contain("Filter by status");
        html.Should().Contain("Filter by user ID");
        html.Should().Contain("Disable");
        html.Should().Contain("Admin user ID");
        html.Should().Contain("Subject type");
        html.Should().Contain("Permissions");
        html.Should().Contain("Watermark template");
        html.Should().Contain("Watermark templates");
        html.Should().Contain("Watermark pattern");
        html.Should().Contain("Offline lease");
        html.Should().Contain("Allow print");
        html.Should().Contain("Revoke");
        html.Should().Contain("Audit events");
        html.Should().Contain("Event type filter");
        html.Should().Contain("Export CSV");
        html.Should().Contain("SIEM webhooks");
        html.Should().Contain("Webhook URL");
        html.Should().Contain("Enabled");
        html.Should().Contain("app.css");
        html.Should().Contain("app.js");
    }

    [Fact]
    public async Task Admin_console_assets_are_served()
    {
        using var client = factory.CreateClient();

        using var cssResponse = await client.GetAsync("/admin/app.css");
        var css = await cssResponse.Content.ReadAsStringAsync();
        using var jsResponse = await client.GetAsync("/admin/app.js");
        var js = await jsResponse.Content.ReadAsStringAsync();

        cssResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        css.Should().Contain(".workspace");
        jsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Should().Contain("sessionStorage");
        js.Should().Contain("X-DRM-Admin-Key");
        js.Should().Contain("/api/admin/users");
        js.Should().Contain("/api/admin/groups");
        js.Should().Contain("/api/admin/files");
        js.Should().Contain("/apply-policy-template");
        js.Should().Contain("applyPolicyTemplateForm");
        js.Should().Contain("/commands/delete-protected-copy");
        js.Should().Contain("deleteProtectedCopy");
        js.Should().Contain("deleteCopyForm");
        js.Should().Contain("/commands?");
        js.Should().Contain("refreshCommands");
        js.Should().Contain("renderCommands");
        js.Should().Contain("commandsBody");
        js.Should().Contain("/share-links");
        js.Should().Contain("createShareLinkForm");
        js.Should().Contain("refreshShareLinks");
        js.Should().Contain("renderShareLinks");
        js.Should().Contain("revokeShareLink");
        js.Should().Contain("shareLinksBody");
        js.Should().Contain("shareUrl");
        js.Should().Contain("/api/admin/policy-templates");
        js.Should().Contain("refreshPolicyTemplates");
        js.Should().Contain("createPolicyTemplateForm");
        js.Should().Contain("/api/admin/watermark-templates");
        js.Should().Contain("refreshWatermarkTemplates");
        js.Should().Contain("createWatermarkTemplateForm");
        js.Should().Contain("/api/admin/policy-simulator");
        js.Should().Contain("simulatePolicy");
        js.Should().Contain("simulatorOutput");
        js.Should().Contain("/api/admin/devices");
        js.Should().Contain("/api/admin/devices/health");
        js.Should().Contain("refreshDevices");
        js.Should().Contain("refreshDeviceHealth");
        js.Should().Contain("renderDeviceHealth");
        js.Should().Contain("devicesBody");
        js.Should().Contain("deviceHealthSummary");
        js.Should().Contain("disableDevice");
        js.Should().Contain("/grants");
        js.Should().Contain("/revoke");
        js.Should().Contain("/api/admin/audit");
        js.Should().Contain("/api/admin/audit.csv");
        js.Should().Contain("refreshAuditEvents");
        js.Should().Contain("downloadAuditCsv");
        js.Should().Contain("/api/admin/siem-webhooks");
        js.Should().Contain("refreshSiemWebhooks");
        js.Should().Contain("createSiemWebhookForm");
        js.Should().Contain("adminUserId");
        js.Should().Contain("revoked");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
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
}
