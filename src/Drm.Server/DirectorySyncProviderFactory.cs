using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server;

/// <summary>
/// Resolves the directory-sync provider for a tenant at request time, AFTER its config has been
/// read — directory sync is per-tenant, so the provider can't be a single DI registration (a second
/// <c>AddScoped&lt;IDirectorySyncService&gt;</c> would be last-wins and silently run the wrong one).
/// </summary>
public interface IDirectorySyncProviderFactory
{
    IDirectorySyncService For(string? provider);
}

public sealed class DirectorySyncProviderFactory(IServiceProvider services) : IDirectorySyncProviderFactory
{
    public IDirectorySyncService For(string? provider)
    {
        var p = string.IsNullOrWhiteSpace(provider) ? "entra" : provider.Trim().ToLowerInvariant();
        return p switch
        {
            "entra" => services.GetRequiredService<EntraIdDirectorySyncService>(),
            // "ldap" lands here once LdapDirectorySyncService ships (increment 3).
            _ => throw new DirectorySyncProviderUnavailableException(p),
        };
    }
}

public sealed class DirectorySyncProviderUnavailableException(string provider)
    : InvalidOperationException($"Directory sync provider '{provider}' is not available yet.")
{
    public string Provider { get; } = provider;
}
