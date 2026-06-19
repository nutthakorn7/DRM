using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drm.Domain;

namespace Drm.Agent.Core;

public interface IDeviceRequestSigner
{
    Task<DeviceRequestSignature?> SignUnwrapAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission,
        CancellationToken cancellationToken);
}

public sealed class DeviceSecretRequestSigner(string deviceSecret) : IDeviceRequestSigner
{
    public Task<DeviceRequestSignature?> SignUnwrapAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceSecret))
        {
            return Task.FromResult<DeviceRequestSignature?>(null);
        }

        var payload = DeviceRequestSigning.UnwrapPayload(
            tenantId,
            fileId,
            userId,
            deviceId,
            requestedPermission);

        return Task.FromResult<DeviceRequestSignature?>(DeviceRequestSigning.Sign(deviceSecret, payload));
    }
}

public sealed class LocalDeviceRequestSigner : IDeviceRequestSigner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeviceRequestSignature?> SignUnwrapAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                DeviceSigningPipe.PipeName(deviceId),
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeout: 1500, cancellationToken);

            await JsonSerializer.SerializeAsync(
                pipe,
                new DeviceSigningPipe.Request(
                    "unwrap",
                    tenantId,
                    fileId,
                    userId,
                    deviceId,
                    requestedPermission),
                JsonOptions,
                cancellationToken);
            await pipe.FlushAsync(cancellationToken);
            pipe.WaitForPipeDrain();

            var response = await JsonSerializer.DeserializeAsync<DeviceSigningPipe.Response>(
                pipe,
                JsonOptions,
                cancellationToken);

            return response?.Signature;
        }
        catch (IOException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public static class DeviceSigningPipe
{
    public static string PipeName(Guid deviceId)
        => $"zcrdrm-agent-sign-{deviceId:N}";

    public sealed record Request(
        string Operation,
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission);

    public sealed record Response(string? ReasonCode, DeviceRequestSignature? Signature)
    {
        public static Response Signed(DeviceRequestSignature signature) => new(null, signature);

        public static Response Error(string reasonCode) => new(reasonCode, null);
    }
}
