using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1033 // IDisposable in fixture — databasePath cleanup is intentional

namespace Drm.Server.Tests;

public sealed class AdminSharedKeyModeTests : IDisposable
{
    private const string AdminApiKey = "shared-key-mode-test";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-sharedkey-mode-{Guid.NewGuid():N}.db");

    // ── Active mode (default) ──────────────────────────────────────────────

    [Fact]
    public async Task Active_mode_shared_key_returns_200_no_deprecation_header()
    {
        using var factory = MakeFactory("Active");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        using var response = await client.GetAsync("/api/admin/identity/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Deprecation").Should().BeFalse();
    }

    // ── Warn mode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Warn_mode_shared_key_returns_200_with_deprecation_header()
    {
        using var factory = MakeFactory("Warn");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        using var response = await client.GetAsync("/api/admin/identity/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Deprecation", out var values).Should().BeTrue();
        values!.Should().Contain("true");
    }

    [Fact]
    public async Task Warn_mode_shared_key_writes_audit_event()
    {
        using var factory = MakeFactory("Warn");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        await client.GetAsync("/api/admin/identity/whoami");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.AuditEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ReasonCode == "shared_key_deprecated_usage");
        evt.Should().NotBeNull();
        evt!.ActorAdminId.Should().Be(AdminSystemRoles.DefaultSuperAdminUserId);
    }

    [Fact]
    public async Task Warn_mode_per_admin_token_returns_200_without_deprecation_header()
    {
        using var factory = MakeFactory("Warn");

        // Bootstrap: create an admin using the shared key
        using var bootstrapClient = factory.CreateClient();
        bootstrapClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        var createBody = new
        {
            email = $"warn-test-{Guid.NewGuid():N}@example.com",
            displayName = "Warn Test",
            roleId = AdminSystemRoles.TenantAdminId,
            tokenLabel = "warn-test"
        };
        using var createResponse = await bootstrapClient.PostAsJsonAsync("/api/admin/identity/admins", createBody);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateAdminBody>();

        // Now use the per-admin token — no deprecation header expected
        using var tokenClient = factory.CreateClient();
        tokenClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, created!.Token);

        using var response = await tokenClient.GetAsync("/api/admin/identity/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Deprecation").Should().BeFalse();
    }

    // ── Disabled mode ──────────────────────────────────────────────────────

    [Fact]
    public async Task Disabled_mode_shared_key_returns_401_shared_key_disabled()
    {
        using var factory = MakeFactory("Disabled");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        using var response = await client.GetAsync("/api/admin/identity/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("shared_key_disabled");
    }

    [Fact]
    public async Task Disabled_mode_per_admin_token_still_works()
    {
        // Use a separate DB so setup (Active) and assertion (Disabled) share data.
        var sharedDb = Path.Combine(Path.GetTempPath(), $"drm-disabled-token-{Guid.NewGuid():N}.db");
        try
        {
            // Create the admin while mode is Active so we can get a valid token.
            using var setupFactory = MakeFactoryWithDb("Active", sharedDb);
            using var setupClient = setupFactory.CreateClient();
            setupClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
            var createBody = new
            {
                email = $"disabled-test-{Guid.NewGuid():N}@example.com",
                displayName = "Disabled Mode Test",
                roleId = AdminSystemRoles.TenantAdminId,
                tokenLabel = "disabled-test"
            };
            using var createResponse = await setupClient.PostAsJsonAsync("/api/admin/identity/admins", createBody);
            var created = await createResponse.Content.ReadFromJsonAsync<CreateAdminBody>();
            var token = created!.Token;
            var adminUserId = created.AdminUserId;

            // Switch to Disabled mode pointing at the same DB — token must still work.
            using var disabledFactory = MakeFactoryWithDb("Disabled", sharedDb);
            using var tokenClient = disabledFactory.CreateClient();
            tokenClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, token);

            using var response = await tokenClient.GetAsync("/api/admin/identity/whoami");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var whoami = await response.Content.ReadFromJsonAsync<WhoamiBody>();
            whoami!.AdminUserId.Should().Be(adminUserId);
        }
        finally
        {
            foreach (var f in new[] { sharedDb, $"{sharedDb}-wal", $"{sharedDb}-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> MakeFactory(string sharedKeyMode) =>
        MakeFactoryWithDb(sharedKeyMode, databasePath);

    private static WebApplicationFactory<Program> MakeFactoryWithDb(string sharedKeyMode, string dbPath) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={dbPath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Security:AdminSharedKeyMode", sharedKeyMode);
            });

    public void Dispose()
    {
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record WhoamiBody(Guid AdminUserId, Guid RoleId, string DisplayName, string Email, List<string> Permissions, bool SharedKeyFallback);
    private sealed record CreateAdminBody(Guid AdminUserId, string Email, string DisplayName, Guid RoleId, string RoleName, Guid TokenId, string Token, DateTimeOffset? TokenExpiresAtUtc);
    private sealed record ErrorBody(string ReasonCode);
}
