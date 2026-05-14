namespace Drm.Container;

public sealed record ProtectedFileHeader(
    int Version,
    Guid TenantId,
    Guid FileId,
    string ContentType,
    DateTimeOffset CreatedAtUtc);
