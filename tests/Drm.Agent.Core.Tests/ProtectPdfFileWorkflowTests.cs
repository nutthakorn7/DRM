using Drm.Agent.Core;
using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class ProtectPdfFileWorkflowTests
{
    [Fact]
    public async Task ProtectPdfFileWorkflow_writes_protected_output_and_records_inventory()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7"u8.ToArray());
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var tenantId = TenantId.New();
        var ownerUserId = UserId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var server = new RecordingServerClient();

        var result = await new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                tenantId,
                ownerUserId,
                sourcePath,
                fileKey,
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        result.DestinationPath.Should().Be($"{sourcePath}.drmx");
        result.OriginalDeleted.Should().BeFalse();
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists(result.DestinationPath).Should().BeTrue();

        await using var output = File.OpenRead(result.DestinationPath);
        var package = ProtectedFileReader.Read(output);
        package.Header.TenantId.Should().Be(tenantId.Value);
        package.Header.FileId.Should().Be(result.FileId);
        package.Decrypt(fileKey).Should().Equal("%PDF-1.7"u8.ToArray());

        var entry = await inventory.FindAsync(tenantId.Value, result.FileId, CancellationToken.None);
        entry.Should().NotBeNull();
        entry!.Path.Should().Be(result.DestinationPath);
        server.RegisteredFiles.Should().ContainSingle(registered =>
            registered.TenantId == tenantId.Value &&
            registered.FileId == result.FileId &&
            registered.OwnerUserId == ownerUserId.Value);
    }

    [Fact]
    public async Task ProtectPdfFileWorkflow_deletes_original_only_after_success_when_requested()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "delete-me.pdf");
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7"u8.ToArray());
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));

        var result = await new ProtectPdfFileWorkflow(new RecordingServerClient(), inventory)
            .ProtectAsync(
                TenantId.New(),
                UserId.New(),
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: true,
                CancellationToken.None);

        result.OriginalDeleted.Should().BeTrue();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(result.DestinationPath).Should().BeTrue();
    }

    [Fact]
    public async Task ProtectPdfFileWorkflow_wraps_file_key_with_server()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7"u8.ToArray());
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var tenantId = TenantId.New();
        var ownerUserId = UserId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var server = new RecordingServerClient();

        var result = await new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                tenantId,
                ownerUserId,
                sourcePath,
                fileKey,
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

        server.WrappedKeys.Should().ContainSingle(wrapped =>
            wrapped.TenantId == tenantId.Value &&
            wrapped.FileId == result.FileId &&
            wrapped.FileKey.SequenceEqual(fileKey));
    }

    [Fact]
    public async Task ProtectPdfFileWorkflow_leaves_original_and_no_output_when_key_wrap_fails()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7"u8.ToArray());
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var server = new RecordingServerClient { FailKeyWrap = true };

        var act = () => new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                TenantId.New(),
                UserId.New(),
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: true,
                CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("key wrap failed");
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists($"{sourcePath}.drmx").Should().BeFalse();
        Directory.GetFiles(tempDirectory.FullName, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ProtectPdfFileWorkflow_leaves_original_and_no_output_when_registration_fails()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(tempDirectory.FullName, "report.pdf");
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.7"u8.ToArray());
        var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
        var server = new RecordingServerClient { FailRegistration = true };

        var act = () => new ProtectPdfFileWorkflow(server, inventory)
            .ProtectAsync(
                TenantId.New(),
                UserId.New(),
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                deleteOriginalAfterProtection: true,
                CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("registration failed");
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists($"{sourcePath}.drmx").Should().BeFalse();
        Directory.GetFiles(tempDirectory.FullName, "*.tmp").Should().BeEmpty();
    }

    private sealed class RecordingServerClient : IDrmServerClient
    {
        public bool FailRegistration { get; init; }

        public bool FailKeyWrap { get; init; }

        public List<(Guid TenantId, Guid FileId, Guid OwnerUserId)> RegisteredFiles { get; } = [];

        public List<(Guid TenantId, Guid FileId, byte[] FileKey)> WrappedKeys { get; } = [];

        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            if (FailRegistration)
            {
                throw new HttpRequestException("registration failed");
            }

            RegisteredFiles.Add((tenantId, fileId, ownerUserId));
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
            if (FailKeyWrap)
            {
                throw new HttpRequestException("key wrap failed");
            }

            WrappedKeys.Add((tenantId, fileId, fileKey.ToArray()));
            return Task.CompletedTask;
        }

        public Task<UnwrappedFileKey> UnwrapFileKeyAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, string requestedPermission, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
