namespace Drm.Domain;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
}

public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.NewGuid());
}

public readonly record struct ProtectedFileId(Guid Value)
{
    public static ProtectedFileId New() => new(Guid.NewGuid());
}
