using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminOutlookIntegrationApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-outlook-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminOutlookIntegrationApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_save_and_retrieve_outlook_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/outlook/config", new
        {
            tenantId,
            enabled = true,
            autoEncryptOutgoingAttachments = true,
            minAttachmentSizeKb = 25,
            skipDomainsCsv = "example.com",
            defaultPolicyTemplateId = (string?)null
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        var loaded = await client.GetFromJsonAsync<OutlookConfigResponse>(
            $"/api/admin/outlook/config?tenantId={tenantId}");
        loaded.Should().NotBeNull();
        loaded!.Enabled.Should().BeTrue();
        loaded.MinAttachmentSizeKb.Should().Be(25);
        loaded.SkipDomainsCsv.Should().Be("example.com");
        loaded.LifetimeProtectedCount.Should().Be(0);
    }

    [Fact]
    public async Task Outlook_protect_attachment_returns_404_when_disabled()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/outlook/config", new
        {
            tenantId,
            enabled = false,
            autoEncryptOutgoingAttachments = true,
            minAttachmentSizeKb = 0,
            skipDomainsCsv = "",
            defaultPolicyTemplateId = (string?)null
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        using var response = await client.PostAsJsonAsync("/api/outlook/protect-attachment", new
        {
            tenantId,
            senderEmail = "alice@corp.com",
            recipients = new[] { "bob@corp.com" },
            attachmentName = "secret.docx",
            attachmentSizeBytes = 1024L
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Outlook_protect_attachment_registers_and_increments_counter()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/outlook/config", new
        {
            tenantId,
            enabled = true,
            autoEncryptOutgoingAttachments = true,
            minAttachmentSizeKb = 0,
            skipDomainsCsv = "",
            defaultPolicyTemplateId = (string?)null
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        using var response = await client.PostAsJsonAsync("/api/outlook/protect-attachment", new
        {
            tenantId,
            senderEmail = "alice@corp.com",
            recipients = new[] { "bob@corp.com" },
            attachmentName = "secret.docx",
            attachmentSizeBytes = 12345L
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProtectAttachmentResponse>();
        body!.Status.Should().Be("protected");
        body.ProtectedFileId.Should().NotBeNull();

        var events = await client.GetFromJsonAsync<List<OutlookEventResponse>>(
            $"/api/admin/outlook/events?tenantId={tenantId}");
        events!.Should().ContainSingle();
        events[0].AttachmentName.Should().Be("secret.docx");
        events[0].Status.Should().Be("protected");

        var config = await client.GetFromJsonAsync<OutlookConfigResponse>(
            $"/api/admin/outlook/config?tenantId={tenantId}");
        config!.LifetimeProtectedCount.Should().Be(1);
    }

    [Fact]
    public async Task Outlook_protect_attachment_skips_when_recipient_domain_excluded()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/outlook/config", new
        {
            tenantId,
            enabled = true,
            autoEncryptOutgoingAttachments = true,
            minAttachmentSizeKb = 0,
            skipDomainsCsv = "corp.com",
            defaultPolicyTemplateId = (string?)null
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        using var response = await client.PostAsJsonAsync("/api/outlook/protect-attachment", new
        {
            tenantId,
            senderEmail = "alice@corp.com",
            recipients = new[] { "bob@corp.com" },
            attachmentName = "internal.docx",
            attachmentSizeBytes = 50000L
        });

        var body = await response.Content.ReadFromJsonAsync<ProtectAttachmentResponse>();
        body!.Status.Should().Be("skipped_recipient_domain");
        body.ProtectedFileId.Should().BeNull();
    }

    [Fact]
    public async Task Outlook_protect_attachment_skips_when_below_min_size()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var save = await client.PutAsJsonAsync("/api/admin/outlook/config", new
        {
            tenantId,
            enabled = true,
            autoEncryptOutgoingAttachments = true,
            minAttachmentSizeKb = 100,
            skipDomainsCsv = "",
            defaultPolicyTemplateId = (string?)null
        });
        save.IsSuccessStatusCode.Should().BeTrue();

        using var response = await client.PostAsJsonAsync("/api/outlook/protect-attachment", new
        {
            tenantId,
            senderEmail = "alice@corp.com",
            recipients = new[] { "bob@external.com" },
            attachmentName = "tiny.txt",
            attachmentSizeBytes = 4096L
        });

        var body = await response.Content.ReadFromJsonAsync<ProtectAttachmentResponse>();
        body!.Status.Should().Be("skipped_below_min_size");
    }

    [Fact]
    public async Task Outlook_addin_manifest_is_served_publicly()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/outlook-addin/manifest.xml");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<OfficeApp");
        body.Should().Contain("DRM Protect Attachments");
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record OutlookConfigResponse(
        Guid TenantId,
        bool Enabled,
        bool AutoEncryptOutgoingAttachments,
        int MinAttachmentSizeKb,
        string SkipDomainsCsv,
        string? DefaultPolicyTemplateId,
        int LifetimeProtectedCount,
        DateTimeOffset UpdatedAtUtc);

    private sealed record OutlookEventResponse(
        long Id,
        string SenderEmail,
        string RecipientCsv,
        string AttachmentName,
        long AttachmentSizeBytes,
        string Status,
        string? ProtectedFileId,
        DateTimeOffset OccurredAtUtc);

    private sealed record ProtectAttachmentResponse(string Status, string? ProtectedFileId);
}
