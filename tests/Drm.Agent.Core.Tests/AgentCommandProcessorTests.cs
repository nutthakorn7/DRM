using Drm.Agent.Core;
using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class AgentCommandProcessorTests
{
    [Fact]
    public async Task AgentCommandProcessor_deletes_verified_protected_copy_and_completes_command()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inventoryPath = Path.Combine(tempDirectory.FullName, "inventory.json");
        var protectedPath = Path.Combine(tempDirectory.FullName, "document.drmx");
        var server = new RecordingCommandServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var identity = new AgentIdentity(tenantId.Value, userId.Value, deviceId.Value);
        var fileKey = EnvelopeCrypto.GenerateKey();
        var protectedBytes = await new ProtectPdfWorkflow(server)
            .ProtectAsync(tenantId, userId, "%PDF-1.7"u8.ToArray(), fileKey, CancellationToken.None);
        await File.WriteAllBytesAsync(protectedPath, protectedBytes);
        var package = ProtectedFileReader.Read(new MemoryStream(protectedBytes, writable: false));
        var inventory = new JsonProtectedFileInventory(inventoryPath);
        await inventory.UpsertAsync(
            new ProtectedFileInventoryEntry(tenantId.Value, package.Header.FileId, protectedPath, DateTimeOffset.UtcNow),
            CancellationToken.None);
        var commandId = Guid.NewGuid();
        server.PendingCommands.Add(DeleteCommand(tenantId.Value, commandId, deviceId.Value, package.Header.FileId));

        await new AgentCommandProcessor(server, inventory)
            .ProcessPendingAsync(identity, CancellationToken.None);

        File.Exists(protectedPath).Should().BeFalse();
        var inventoryEntry = await inventory.FindAsync(tenantId.Value, package.Header.FileId, CancellationToken.None);
        inventoryEntry.Should().BeNull();
        server.Completions.Should().ContainSingle(completion =>
            completion.CommandId == commandId &&
            completion.Status == "Completed" &&
            completion.ReasonCode == "deleted");
    }

    [Fact]
    public async Task AgentCommandProcessor_does_not_delete_file_when_container_verification_fails()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inventoryPath = Path.Combine(tempDirectory.FullName, "inventory.json");
        var protectedPath = Path.Combine(tempDirectory.FullName, "not-a-container.drmx");
        await File.WriteAllTextAsync(protectedPath, "plain text");
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var server = new RecordingCommandServerClient();
        var inventory = new JsonProtectedFileInventory(inventoryPath);
        await inventory.UpsertAsync(
            new ProtectedFileInventoryEntry(tenantId, fileId, protectedPath, DateTimeOffset.UtcNow),
            CancellationToken.None);
        var commandId = Guid.NewGuid();
        server.PendingCommands.Add(DeleteCommand(tenantId, commandId, deviceId, fileId));

        await new AgentCommandProcessor(server, inventory)
            .ProcessPendingAsync(new AgentIdentity(tenantId, Guid.NewGuid(), deviceId), CancellationToken.None);

        File.Exists(protectedPath).Should().BeTrue();
        server.Completions.Should().ContainSingle(completion =>
            completion.CommandId == commandId &&
            completion.Status == "Failed" &&
            completion.ReasonCode == "verification_failed");
    }

    [Fact]
    public async Task AgentCommandProcessor_reports_not_found_when_inventory_entry_is_missing()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var server = new RecordingCommandServerClient();
        server.PendingCommands.Add(DeleteCommand(tenantId, commandId, deviceId, fileId));

        await new AgentCommandProcessor(server, new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json")))
            .ProcessPendingAsync(new AgentIdentity(tenantId, Guid.NewGuid(), deviceId), CancellationToken.None);

        server.Completions.Should().ContainSingle(completion =>
            completion.CommandId == commandId &&
            completion.Status == "Failed" &&
            completion.ReasonCode == "not_found");
    }

    private static AgentCommand DeleteCommand(Guid tenantId, Guid commandId, Guid deviceId, Guid fileId)
        => new(
            tenantId,
            commandId,
            deviceId,
            fileId,
            "DeleteProtectedCopy",
            "Pending",
            "queued",
            DateTimeOffset.UtcNow,
            null);

    private sealed class RecordingCommandServerClient : IDrmServerClient
    {
        public List<AgentCommand> PendingCommands { get; } = [];

        public List<(Guid CommandId, string Status, string ReasonCode)> Completions { get; } = [];

        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OpenDecision(true, "allowed", null, Permission.View, DateTimeOffset.UtcNow.AddMinutes(5)));
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
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(AgentIdentity identity, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AgentCommand>>(PendingCommands);
        }

        public Task<AgentCommand> CompleteCommandAsync(AgentIdentity identity, Guid commandId, AgentCommandCompletion completion, CancellationToken cancellationToken)
        {
            Completions.Add((commandId, completion.Status, completion.ReasonCode));
            return Task.FromResult(PendingCommands.Single(command => command.CommandId == commandId) with
            {
                Status = completion.Status,
                ReasonCode = completion.ReasonCode,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
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
