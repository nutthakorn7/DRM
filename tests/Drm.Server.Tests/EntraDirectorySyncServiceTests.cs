using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drm.Server.Tests;

public sealed class EntraDirectorySyncServiceTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-entra-svc-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Sync_imports_users_groups_and_memberships()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var tenantId = Guid.NewGuid();
        db.TenantDirectorySyncConfigs.Add(new TenantDirectorySyncConfigEntity
        {
            TenantId = tenantId,
            EntraTenantId = "contoso.onmicrosoft.com",
            ClientId = "client-id",
            ClientSecret = "secret",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeGraphHandler());
        var service = new EntraIdDirectorySyncService(db, factory, NullLogger<EntraIdDirectorySyncService>.Instance);

        var result = await service.SyncAsync(tenantId, CancellationToken.None);

        result.UsersUpserted.Should().Be(1);
        result.GroupsUpserted.Should().Be(1);
        result.MembershipsUpserted.Should().Be(1);

        var users = await db.TenantUsers.Where(u => u.TenantId == tenantId).ToListAsync();
        users.Should().ContainSingle(u =>
            u.Email == "alice@contoso.com" &&
            u.DisplayName == "Alice Smith" &&
            u.UserId == new Guid("10000000-0000-0000-0000-000000000001"));

        var groups = await db.TenantGroups.Where(g => g.TenantId == tenantId).ToListAsync();
        groups.Should().ContainSingle(g =>
            g.Name == "Engineering" &&
            g.GroupId == new Guid("20000000-0000-0000-0000-000000000001"));

        var members = await db.GroupMembers.Where(m => m.TenantId == tenantId).ToListAsync();
        members.Should().ContainSingle(m =>
            m.GroupId == new Guid("20000000-0000-0000-0000-000000000001") &&
            m.UserId == new Guid("10000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public async Task Sync_throws_when_config_missing()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var factory = new FakeHttpClientFactory(new FakeGraphHandler());
        var service = new EntraIdDirectorySyncService(db, factory, NullLogger<EntraIdDirectorySyncService>.Instance);

        var act = () => service.SyncAsync(Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Sync_updates_existing_user_display_name()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var tenantId = Guid.NewGuid();
        var userId = new Guid("10000000-0000-0000-0000-000000000001");

        db.TenantDirectorySyncConfigs.Add(new TenantDirectorySyncConfigEntity
        {
            TenantId = tenantId,
            EntraTenantId = "contoso.onmicrosoft.com",
            ClientId = "client-id",
            ClientSecret = "secret",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = "alice@contoso.com",
            DisplayName = "Old Name",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeGraphHandler());
        var service = new EntraIdDirectorySyncService(db, factory, NullLogger<EntraIdDirectorySyncService>.Instance);

        var result = await service.SyncAsync(tenantId, CancellationToken.None);

        result.UsersUpserted.Should().Be(0);
        var user = await db.TenantUsers.SingleAsync(u => u.TenantId == tenantId && u.UserId == userId);
        user.DisplayName.Should().Be("Alice Smith");
    }

    [Fact]
    public async Task Sync_updates_last_sync_status_on_config()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var tenantId = Guid.NewGuid();
        db.TenantDirectorySyncConfigs.Add(new TenantDirectorySyncConfigEntity
        {
            TenantId = tenantId,
            EntraTenantId = "contoso.onmicrosoft.com",
            ClientId = "client-id",
            ClientSecret = "secret",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var factory = new FakeHttpClientFactory(new FakeGraphHandler());
        var service = new EntraIdDirectorySyncService(db, factory, NullLogger<EntraIdDirectorySyncService>.Instance);

        await service.SyncAsync(tenantId, CancellationToken.None);

        var config = await db.TenantDirectorySyncConfigs.SingleAsync(c => c.TenantId == tenantId);
        config.LastSyncStatus.Should().Be("ok");
        config.LastSyncAtUtc.Should().NotBeNull();
    }

    public void Dispose()
    {
        foreach (var f in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

file sealed class FakeGraphHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!.ToString();
        string content;

        if (uri.Contains("/oauth2/v2.0/token"))
        {
            content = """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""";
        }
        else if (uri.Contains("/v1.0/groups/20000000-0000-0000-0000-000000000001/members"))
        {
            content = """{"value":[{"id":"10000000-0000-0000-0000-000000000001"}]}""";
        }
        else if (uri.Contains("/v1.0/groups"))
        {
            content = """{"value":[{"id":"20000000-0000-0000-0000-000000000001","displayName":"Engineering"}]}""";
        }
        else if (uri.Contains("/v1.0/users"))
        {
            content = """{"value":[{"id":"10000000-0000-0000-0000-000000000001","mail":"alice@contoso.com","displayName":"Alice Smith","userPrincipalName":"alice@contoso.com"}]}""";
        }
        else
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

file sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}
