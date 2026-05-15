using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class OpenProtectedPdfFileWorkflowTests
{
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

        opened.Content.Should().Equal("%PDF-1.7 open"u8.ToArray());
        opened.Watermark.Should().Contain(userId.Value.ToString("N"));
    }

    [Fact]
    public async Task OpenProtectedPdfFileWorkflow_denies_when_key_is_missing()
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

        var act = () => new OpenProtectedPdfFileWorkflow(
                server,
                new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "missing-keys.json")))
            .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access denied: file_key_missing");
    }

    private sealed class AllowingServerClient : IDrmServerClient
    {
        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OpenDecision(
                true,
                "allowed",
                "{user} {file}",
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
            throw new NotSupportedException();
        }

        public Task<byte[]> UnwrapFileKeyAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, string requestedPermission, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
