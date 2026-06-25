using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// LDAP-sync increment 2: config schema + secret encryption at rest + per-tenant provider factory.
/// (The LDAP connector itself is increment 3.)
/// </summary>
public sealed class DirectorySyncLdapConfigTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-ldapcfg-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public DirectorySyncLdapConfigTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
            b.UseSetting("Drm:Mode", "OnPrem");
            b.UseSetting("Drm:KeyWrapping:MasterKeyBase64",
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(v => (byte)v).ToArray()));
        });
    }

    [Fact]
    public void Secret_protector_round_trips_and_is_tenant_scoped()
    {
        using var scope = factory.Services.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<IDirectorySecretProtector>();
        var t = Guid.NewGuid();

        var stored = p.Protect(t, "bind-password");
        stored.Should().StartWith("drmenc1:").And.NotContain("bind-password");
        p.Unprotect(t, stored).Should().Be("bind-password");

        // Wrong tenant cannot decrypt (AAD binds ciphertext to its tenant).
        Action wrongTenant = () => p.Unprotect(Guid.NewGuid(), stored);
        wrongTenant.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Secret_protector_passes_through_legacy_plaintext()
    {
        using var scope = factory.Services.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<IDirectorySecretProtector>();
        // A value provisioned before encryption has no prefix — must keep working until next save.
        p.Unprotect(Guid.NewGuid(), "legacy-plaintext").Should().Be("legacy-plaintext");
        p.Protect(Guid.NewGuid(), "").Should().BeEmpty();
    }

    [Fact]
    public void Provider_factory_resolves_entra_and_rejects_ldap_until_implemented()
    {
        using var scope = factory.Services.CreateScope();
        var f = scope.ServiceProvider.GetRequiredService<IDirectorySyncProviderFactory>();

        f.For("entra").Should().BeOfType<EntraIdDirectorySyncService>();
        f.For(null).Should().BeOfType<EntraIdDirectorySyncService>("empty provider defaults to entra");

        Action ldap = () => f.For("ldap");
        ldap.Should().Throw<DirectorySyncProviderUnavailableException>().Which.Provider.Should().Be("ldap");
    }

    [Fact]
    public async Task Ldap_config_saves_encrypted_password_write_only()
    {
        using var client = factory.CreateClient();
        var t = Guid.NewGuid();

        using var put = await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId = t, entraTenantId = "", clientId = "", clientSecret = "",
            provider = "ldap", ldapHost = "dc01.corp.local", ldapBindDn = "CN=svc,DC=corp,DC=local",
            ldapBindPassword = "p@ss", ldapBaseDn = "DC=corp,DC=local",
        });
        put.StatusCode.Should().Be(HttpStatusCode.Created);

        var cfg = await client.GetFromJsonAsync<LdapConfigResponse>($"/api/admin/directory/config?tenantId={t}");
        cfg!.Provider.Should().Be("ldap");
        cfg.LdapHost.Should().Be("dc01.corp.local");
        cfg.LdapBindDn.Should().Be("CN=svc,DC=corp,DC=local");
        cfg.LdapBindPasswordSet.Should().BeTrue();

        // Password is encrypted at rest, never echoed.
        await using (var s = factory.Services.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var enc = await db.TenantDirectorySyncConfigs.Where(c => c.TenantId == t)
                .Select(c => c.LdapBindPasswordEncrypted).SingleAsync();
            enc.Should().StartWith("drmenc1:").And.NotContain("p@ss");
        }

        // Re-save WITHOUT a password (write-only) keeps the existing one.
        using var put2 = await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId = t, entraTenantId = "", clientId = "", clientSecret = "",
            provider = "ldap", ldapHost = "dc02.corp.local", ldapBaseDn = "DC=corp,DC=local",
        });
        put2.StatusCode.Should().Be(HttpStatusCode.OK);
        var cfg2 = await client.GetFromJsonAsync<LdapConfigResponse>($"/api/admin/directory/config?tenantId={t}");
        cfg2!.LdapHost.Should().Be("dc02.corp.local");
        cfg2.LdapBindPasswordSet.Should().BeTrue("omitting the password must not wipe it");
    }

    [Fact]
    public async Task Sync_on_ldap_provider_returns_501_until_connector_ships()
    {
        using var client = factory.CreateClient();
        var t = Guid.NewGuid();
        using var put = await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId = t, entraTenantId = "", clientId = "", clientSecret = "", provider = "ldap",
            ldapHost = "dc01", ldapBaseDn = "DC=corp,DC=local",
        });
        put.EnsureSuccessStatusCode();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/directory/sync")
        {
            Content = JsonContent.Create(new { tenantId = t }),
        };
        req.Headers.Add("X-DRM-Tenant-Id", t.ToString());
        using var sync = await client.SendAsync(req);
        sync.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record LdapConfigResponse(
        string Provider, string LdapHost, string LdapBindDn, bool LdapBindPasswordSet, string LdapBaseDn);
}
