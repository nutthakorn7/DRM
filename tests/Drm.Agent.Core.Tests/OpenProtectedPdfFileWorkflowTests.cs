using System.Net;
using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class OpenProtectedPdfFileWorkflowTests
{
    [Fact]
    public async Task OpenProtectedFileWorkflow_opens_non_pdf_file_and_returns_content_type()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "contract.docx");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var keyStore = new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "keys.json"));
        var server = new AllowingServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "office bytes"u8.ToArray());

        var protectedFile = await new ProtectFileWorkflow(server, inventory, keyStore)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                ProtectFilePolicyOptions.Default,
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        var opened = await new OpenProtectedFileWorkflow(server, keyStore)
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        opened.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        opened.Content.Should().Equal("office bytes"u8.ToArray());
        opened.TenantId.Should().Be(tenantId.Value);
        opened.FileId.Should().Be(protectedFile.FileId);
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_loads_key_and_opens_protected_file()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var keyStore = new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "keys.json"));
        var server = new AllowingServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 open"u8.ToArray());

        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory, keyStore)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        var opened = await new OpenProtectedPdfFileWorkflow(server, keyStore)
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        opened.TenantId.Should().Be(tenantId.Value);
        opened.FileId.Should().Be(protectedFile.FileId);
        opened.Content.Should().Equal("%PDF-1.7 open"u8.ToArray());
        opened.Watermark.Should().Contain(userId.Value.ToString("N"));
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_uses_server_unwrap_when_local_key_is_missing()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var server = new AllowingServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 open"u8.ToArray());
        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        var opened = await new OpenProtectedPdfFileWorkflow(
                server,
                new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "missing-keys.json")))
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        opened.Content.Should().Equal("%PDF-1.7 open"u8.ToArray());
        server.UnwrapRequests.Should().ContainSingle(request =>
            request.TenantId == tenantId.Value &&
            request.FileId == protectedFile.FileId &&
            request.UserId == userId.Value &&
            request.DeviceId == deviceId.Value &&
            request.RequestedPermission == "View");
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_does_not_fallback_to_local_key_when_server_denies_unwrap()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var keyStore = new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "keys.json"));
        var server = new AllowingServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 open"u8.ToArray());
        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory, keyStore)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);
        server.DenyUnwrap = true;

        var act = () => new OpenProtectedPdfFileWorkflow(server, keyStore)
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access denied: file_key_denied");
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_falls_back_to_local_key_when_unwrap_transport_fails()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var keyStore = new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "keys.json"));
        var server = new AllowingServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 fallback"u8.ToArray());
        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory, keyStore)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);
        server.FailUnwrapTransport = true;

        var opened = await new OpenProtectedPdfFileWorkflow(server, keyStore)
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        opened.Content.Should().Equal("%PDF-1.7 fallback"u8.ToArray());
        server.UnwrapRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_denies_when_server_unavailable_and_local_key_is_missing()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var server = new AllowingServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 open"u8.ToArray());
        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);
        server.FailUnwrapTransport = true;

        var act = () => new OpenProtectedPdfFileWorkflow(
                server,
                new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "missing-keys.json")))
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access denied: file_key_missing");
        server.UnwrapRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_uses_unwrap_decision_without_second_policy_call()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var cache = new RecordingPolicyDecisionCache();
        var server = new AllowingServerClient { FailDecision = true };
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 metadata"u8.ToArray());
        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        var opened = await new OpenProtectedPdfFileWorkflow(
                server,
                new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "missing-keys.json")),
                cache)
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        opened.Content.Should().Equal("%PDF-1.7 metadata"u8.ToArray());
        opened.Watermark.Should().Contain(userId.Value.ToString("N"));
        server.DecisionRequests.Should().BeEmpty();
        cache.StoredEntries.Should().ContainSingle(entry =>
            entry.Key.TenantId == tenantId.Value &&
            entry.Key.FileId == protectedFile.FileId &&
            entry.Key.UserId == userId.Value &&
            entry.Key.DeviceId == deviceId.Value &&
            entry.AllowedPermissions == Permission.View &&
            entry.OfflineLeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_renders_watermark_alias_placeholders()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var server = new AllowingServerClient
        {
            WatermarkTemplate = "user:{userId} file:{fileId} time:{time}"
        };
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7 watermark"u8.ToArray());
        var protectedFile = await new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                tenantId,
                userId,
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        var opened = await new OpenProtectedPdfFileWorkflow(
                server,
                new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "missing-keys.json")))
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        opened.Watermark.Should().Contain(userId.Value.ToString("N"));
        opened.Watermark.Should().Contain(protectedFile.FileId.ToString("N"));
        opened.Watermark.Should().NotContain("{userId}");
        opened.Watermark.Should().NotContain("{fileId}");
        opened.Watermark.Should().NotContain("{time}");
    }

    private sealed class AllowingServerClient : IDrmServerClient
    {
        private readonly Dictionary<(Guid TenantId, Guid FileId), byte[]> fileKeys = [];

        public string WatermarkTemplate { get; init; } = "{user} {file}";

        public bool DenyUnwrap { get; set; }

        public bool FailUnwrapTransport { get; set; }

        public bool FailDecision { get; init; }

        public List<(Guid TenantId, Guid FileId, Guid UserId, Guid DeviceId, string RequestedPermission)> UnwrapRequests { get; } = [];

        public List<(Guid TenantId, Guid FileId, Guid UserId, Guid DeviceId, Permission Permission)> DecisionRequests { get; } = [];

        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            DecisionRequests.Add((tenantId, fileId, userId, deviceId, permission));
            if (FailDecision)
            {
                throw new InvalidOperationException("decision should not be called");
            }

            return Task.FromResult(new OpenDecision(
                true,
                "allowed",
                WatermarkTemplate,
                Permission.View,
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public Task<AgentDeviceRegistration> RegisterDeviceAsync(AgentIdentity identity, string hostname, string operatingSystem, string agentVersion, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AgentHeartbeat> RecordHeartbeatAsync(AgentIdentity identity, string status, string agentVersion, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(AgentIdentity identity, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AgentCommand> CompleteCommandAsync(AgentIdentity identity, Guid commandId, AgentCommandCompletion completion, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task WrapFileKeyAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken)
        {
            fileKeys[(tenantId, fileId)] = fileKey.ToArray();
            return Task.CompletedTask;
        }

        public Task<UnwrappedFileKey> UnwrapFileKeyAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, string requestedPermission, CancellationToken cancellationToken)
        {
            UnwrapRequests.Add((tenantId, fileId, userId, deviceId, requestedPermission));
            if (DenyUnwrap)
            {
                throw new HttpRequestException("unwrap denied", null, HttpStatusCode.Forbidden);
            }

            if (FailUnwrapTransport)
            {
                throw new HttpRequestException("server unavailable");
            }

            if (!fileKeys.TryGetValue((tenantId, fileId), out var fileKey))
            {
                throw new HttpRequestException("file key missing", null, HttpStatusCode.NotFound);
            }

            return Task.FromResult(new UnwrappedFileKey(
                fileKey.ToArray(),
                Permission.View,
                WatermarkTemplate,
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class RecordingPolicyDecisionCache : IPolicyDecisionCache
    {
        public List<CachedPolicyDecision> StoredEntries { get; } = [];

        public Task StoreAsync(CachedPolicyDecision decision, CancellationToken cancellationToken)
        {
            StoredEntries.Add(decision);
            return Task.CompletedTask;
        }

        public Task<CachedPolicyDecision?> TryGetAllowedAsync(PolicyDecisionCacheKey key, DateTimeOffset atUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult<CachedPolicyDecision?>(null);
        }
    }
}
