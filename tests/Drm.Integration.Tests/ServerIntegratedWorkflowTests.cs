using System.Net;
using System.Net.Http.Json;
using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Integration.Tests;

public sealed class ServerIntegratedWorkflowTests
{
    [Fact]
    public async Task Agent_quick_send_protects_file_and_creates_external_share_link()
    {
        // Stage 13: end-to-end chain the agent's Quick Send button runs.
        // ProtectAsync encrypts + registers + writes .drmx, then we mint
        // an external share-link against the registered fileId — same
        // path the recipient verification email uses to authenticate.
        // Guards against the Stage 12 incident where token hash format
        // mismatched silently across endpoints.
        var dbPath = Path.Combine(Path.GetTempPath(), $"drm-quick-send-{Guid.NewGuid():N}.db");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"drm-quick-source-{Guid.NewGuid():N}.pdf");
        var destPath = $"{sourcePath}.drmx";
        WebApplicationFactory<Program>? factory = null;
        HttpClient? httpClient = null;

        try
        {
            await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 stage13 quick-send smoke"u8.ToArray());
            factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={dbPath}");
                    builder.UseSetting("Drm:Mode", "OnPrem");
                });
            httpClient = factory.CreateClient();
            var serverClient = new DrmServerClient(httpClient);
            var inventory = new InMemoryProtectedFileInventory();
            var keyStore = new InMemoryFileKeyStore();
            var workflow = new ProtectFileWorkflow(serverClient, inventory, keyStore);

            var tenantId = TenantId.New();
            var userId = UserId.New();
            var fileKey = EnvelopeCrypto.GenerateKey();
            var permissions = Permission.View | Permission.Print | Permission.Copy;
            var expiresAtUtc = DateTimeOffset.UtcNow.AddHours(168);

            var protectResult = await workflow.ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                fileKey,
                new ProtectFilePolicyOptions(permissions, PolicyTemplateId: null, Recipients: []),
                deleteOriginalAfterProtection: false,
                CancellationToken.None,
                fileExpiresAtUtc: expiresAtUtc);

            protectResult.FileId.Should().NotBe(Guid.Empty);
            File.Exists(protectResult.DestinationPath).Should().BeTrue("agent must write .drmx next to source");

            using var shareLinkResponse = await httpClient.PostAsJsonAsync(
                $"/api/admin/files/{protectResult.FileId}/share-links",
                new
                {
                    tenantId = tenantId.Value,
                    adminUserId = userId.Value,
                    guestEmail = "recipient@example.com",
                    expiresAtUtc,
                    maxUses = 1,
                });

            shareLinkResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var payload = await shareLinkResponse.Content.ReadFromJsonAsync<ShareLinkResponseShape>();
            payload.Should().NotBeNull();
            payload!.ShareUrl.Should().Contain("/share/").And.Contain(payload.AccessToken);
            payload.FileId.Should().Be(protectResult.FileId);
            payload.GuestEmail.Should().Be("recipient@example.com");

            // /share/ recipient flow shares the same token format —
            // verification/start AND redeem must both accept the access
            // token we just got. Both endpoints existed independently in
            // Stage 12's hex-vs-base64 drift; assert both halves of the
            // round-trip so a future format change can't silently break
            // one and leave the other green.
            using var verificationStart = await httpClient.PostAsJsonAsync(
                "/api/share-links/verification/start",
                new
                {
                    tenantId = tenantId.Value,
                    accessToken = payload.AccessToken,
                    guestEmail = "recipient@example.com",
                });
            verificationStart.StatusCode.Should().Be(HttpStatusCode.OK);

            using var redeem = await httpClient.PostAsJsonAsync(
                "/api/share-links/redeem",
                new
                {
                    tenantId = tenantId.Value,
                    accessToken = payload.AccessToken,
                    guestEmail = "recipient@example.com",
                });
            redeem.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            httpClient?.Dispose();
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }

            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(destPath)) File.Delete(destPath);
            DeleteSqliteFiles(dbPath);
        }
    }

    private sealed record ShareLinkResponseShape(Guid FileId, string GuestEmail, string AccessToken, string ShareUrl);

    private sealed class InMemoryProtectedFileInventory : IProtectedFileInventory
    {
        public Task UpsertAsync(ProtectedFileInventoryEntry entry, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<ProtectedFileInventoryEntry?> FindAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
            => Task.FromResult<ProtectedFileInventoryEntry?>(null);
        public Task RemoveAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class InMemoryFileKeyStore : IFileKeyStore
    {
        public Task SaveAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<byte[]?> LoadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
            => Task.FromResult<byte[]?>(null);
    }

    [Fact]
    public async Task Agent_core_can_protect_and_open_against_management_server()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"drm-integrated-{Guid.NewGuid():N}.db");
        WebApplicationFactory<Program>? factory = null;
        HttpClient? httpClient = null;

        try
        {
            factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={dbPath}");
                    builder.UseSetting("Drm:Mode", "OnPrem");
                });
            httpClient = factory.CreateClient();
            var serverClient = new DrmServerClient(httpClient);
            var tenantId = TenantId.New();
            var userId = UserId.New();
            var deviceId = DeviceId.New();
            var fileKey = EnvelopeCrypto.GenerateKey();
            var pdfBytes = "%PDF-1.7 smoke"u8.ToArray();

            var protectedBytes = await new ProtectPdfWorkflow(serverClient)
                .ProtectAsync(tenantId, userId, pdfBytes, fileKey, CancellationToken.None);

            var opened = await new OpenProtectedPdfWorkflow(serverClient)
                .OpenAsync(protectedBytes, userId, deviceId, fileKey, CancellationToken.None);

            opened.Content.Should().Equal(pdfBytes);
            opened.Permissions.Should().HaveFlag(Permission.View);
        }
        finally
        {
            httpClient?.Dispose();
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }

            DeleteSqliteFiles(dbPath);
        }
    }

    private static void DeleteSqliteFiles(string dbPath)
    {
        foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
