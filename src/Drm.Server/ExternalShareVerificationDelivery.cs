namespace Drm.Server;

public interface IExternalShareVerificationSender
{
    Task SendAsync(ExternalShareVerificationMessage message, CancellationToken cancellationToken);
}

public sealed record ExternalShareVerificationMessage(
    Guid TenantId,
    Guid ShareLinkId,
    Guid VerificationId,
    string GuestEmail,
    string Code,
    DateTimeOffset ExpiresAtUtc);

public sealed class NoopExternalShareVerificationSender : IExternalShareVerificationSender
{
    public Task SendAsync(ExternalShareVerificationMessage message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
