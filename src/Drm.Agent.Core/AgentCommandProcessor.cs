using System.Text.Json;
using Drm.Container;

namespace Drm.Agent.Core;

public sealed class AgentCommandProcessor(IDrmServerClient serverClient, IProtectedFileInventory inventory)
{
    public async Task ProcessPendingAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        var commands = await serverClient.GetPendingCommandsAsync(identity, cancellationToken);
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (command.CommandType == "DeleteProtectedCopy")
            {
                await ProcessDeleteProtectedCopyAsync(identity, command, cancellationToken);
                continue;
            }

            await serverClient.CompleteCommandAsync(
                identity,
                command.CommandId,
                new AgentCommandCompletion("Failed", "unsupported_command"),
                cancellationToken);
        }
    }

    private async Task ProcessDeleteProtectedCopyAsync(
        AgentIdentity identity,
        AgentCommand command,
        CancellationToken cancellationToken)
    {
        var entry = await inventory.FindAsync(command.TenantId, command.FileId, cancellationToken);
        if (entry is null || !File.Exists(entry.Path))
        {
            await CompleteAsync(identity, command.CommandId, "Failed", "not_found", cancellationToken);
            return;
        }

        if (!IsVerifiedProtectedContainer(entry.Path, command.TenantId, command.FileId))
        {
            await CompleteAsync(identity, command.CommandId, "Failed", "verification_failed", cancellationToken);
            return;
        }

        File.Delete(entry.Path);
        await inventory.RemoveAsync(command.TenantId, command.FileId, cancellationToken);
        await CompleteAsync(identity, command.CommandId, "Completed", "deleted", cancellationToken);
    }

    private async Task CompleteAsync(
        AgentIdentity identity,
        Guid commandId,
        string status,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        await serverClient.CompleteCommandAsync(
            identity,
            commandId,
            new AgentCommandCompletion(status, reasonCode),
            cancellationToken);
    }

    private static bool IsVerifiedProtectedContainer(string path, Guid tenantId, Guid fileId)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var package = ProtectedFileReader.Read(stream);
            return package.Header.TenantId == tenantId && package.Header.FileId == fileId;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
