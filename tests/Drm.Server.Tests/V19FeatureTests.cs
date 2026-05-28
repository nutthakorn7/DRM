using System.Net;
using System.Net.Http.Json;
using Drm.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Integration tests for v1.9 features:
///   1. IP allowlist — CRUD rules, enforcement blocks non-allowlisted IPs before key unwrap
///   2. Device trust — config upsert, deny when heartbeat stale
///   3. Time-windowed grants — grant valid only in window; decisions tested via simulator
/// </summary>
public sealed class V19FeatureTests : IDisposable
{
    private const string AdminKey = "v19-test-key";

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"drm-v19-{Guid.NewGuid():N}.db");

    private readonly WebApplicationFactory<Program> factory;

    public V19FeatureTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminKey);
                builder.UseSetting("Drm:Security:AuditChainKey",
                    "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20");
            });
    }

    public void Dispose()
    {
        factory.Dispose();
        if (File.Exists(databasePath)) File.Delete(databasePath);
    }

    private HttpClient AdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminKey);
        return client;
    }

    private async Task<(Guid tenantId, Guid userId)> SeedTenantUserAsync(string prefix = "v19")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            Name = prefix + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "V19 Tenant",
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = $"alice@corp.example",
            DisplayName = "Alice",
            Active = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (tenantId, userId);
    }

    /// <summary>
    /// Seeds a file owned by <paramref name="ownerId"/> (Permissions.None so only explicit grants
    /// control access) plus a grant for <paramref name="granteeId"/> with an optional time window.
    /// Also seeds a file key so the unwrap endpoint can proceed.
    /// </summary>
    private async Task<Guid> SeedFileWithGrantAsync(
        Guid tenantId,
        Guid ownerId,
        Guid granteeId,
        DateTimeOffset? validFromUtc = null,
        DateTimeOffset? validUntilUtc = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileId = Guid.NewGuid();

        // Permissions.None ensures owner fallback does not bypass grant window checks
        db.ProtectedFiles.Add(new ProtectedFileEntity
        {
            Id = fileId,
            TenantId = tenantId,
            OwnerUserId = ownerId,
            ContentType = "application/pdf",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            Revoked = false,
            Permissions = Domain.Permission.None,
            WatermarkTemplate = "",
            OfflineLeaseMinutes = 0
        });
        db.FileKeys.Add(new FileKeyEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            WrappedKeyNonceBase64 = "AAAAAAAAAAAAAAAA",
            WrappedKeyCiphertextBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            WrappedKeyTagBase64 = "AAAAAAAAAAAAAAAAAAAAAA==",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        db.FileGrants.Add(new FileGrantEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            SubjectType = "User",
            SubjectId = granteeId,
            Permissions = "View",
            ValidFromUtc = validFromUtc,
            ValidUntilUtc = validUntilUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private async Task SeedDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid userId,
        DateTimeOffset? lastHeartbeat = null,
        bool domainJoined = false,
        string domainName = "",
        string windowsUser = "",
        string? deviceSecret = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AgentDevices.Add(new AgentDeviceEntity
        {
            TenantId = tenantId,
            DeviceId = deviceId,
            UserId = userId,
            Hostname = "test-host",
            OperatingSystem = "Windows",
            AgentVersion = "1.0",
            Status = "active",
            RegisteredAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastHeartbeatAtUtc = lastHeartbeat,
            DomainJoined = domainJoined,
            DomainName = domainName,
            WindowsUser = windowsUser,
            DeviceSigningKeyHashBase64 = string.IsNullOrWhiteSpace(deviceSecret)
                ? string.Empty
                : DeviceRequestSigning.DeriveSigningKeyHashBase64(deviceSecret),
            DeviceSigningKeyUpdatedAtUtc = string.IsNullOrWhiteSpace(deviceSecret)
                ? null
                : DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Uses the policy simulator (no AES-GCM, no audit write) to evaluate access.
    /// Returns the JSON body from POST /api/admin/policy-simulator.
    /// </summary>
    private async Task<SimulatorResult?> SimulateAsync(
        Guid tenantId, Guid fileId, Guid userId, Guid deviceId)
    {
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        return await client.PostAsJsonAsync("/api/admin/policy-simulator", new
        {
            tenantId,
            fileId,
            userId,
            deviceId,
            requestedPermission = "View"
        }).ContinueWith(t => t.Result.Content.ReadFromJsonAsync<SimulatorResult>()).Unwrap();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. IP allowlist CRUD
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddIpRule_returns_201_with_rule()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/ip-allowlist",
            new { cidr = "10.0.0.0/8", label = "Office network" });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<IpRuleRow>();
        body!.Cidr.Should().Be("10.0.0.0/8");
        body.Label.Should().Be("Office network");
    }

    [Fact]
    public async Task AddIpRule_rejects_invalid_cidr()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/ip-allowlist",
            new { cidr = "not-a-cidr", label = "" });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteIpRule_removes_rule()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var added = await (await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/ip-allowlist",
            new { cidr = "192.168.0.0/16", label = "" }))
            .Content.ReadFromJsonAsync<IpRuleRow>();

        var del = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/ip-allowlist/{added!.RuleId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listRes = await client.GetAsync($"/api/admin/tenants/{tenantId}/ip-allowlist");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listRes.Content.ReadFromJsonAsync<List<IpRuleRow>>();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task IpAllowlist_denies_unwrap_when_client_ip_not_in_list()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        // Add an allowlist rule that does NOT include the loopback IP used by the test client
        using var adminClient = AdminClient();
        adminClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        await adminClient.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/ip-allowlist",
            new { cidr = "203.0.113.0/24", label = "Remote office" }); // not loopback

        // Unwrap should be denied at the IP check stage (before the decision service)
        using var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId,
            deviceId = Guid.Empty,
            requestedPermission = "View"
        });

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("ip_not_allowed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Device trust enforcement
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PutDeviceTrust_upserts_config()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/device-trust",
            new { enabled = true, requiredCheckinDays = 3 });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<DeviceTrustRow>();
        body!.Enabled.Should().BeTrue();
        body.RequiredCheckinDays.Should().Be(3);
    }

    [Fact]
    public async Task PutDeviceTrust_accepts_ad_domain_requirements()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/device-trust",
            new
            {
                enabled = true,
                requiredCheckinDays = 3,
                requireDomainJoined = true,
                allowedAdDomains = new[] { "CORP", "ENGINEERING" }
            });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<DeviceTrustRow>();
        body!.Enabled.Should().BeTrue();
        body.RequireDomainJoined.Should().BeTrue();
        body.AllowedAdDomains.Should().BeEquivalentTo("CORP", "ENGINEERING");
    }

    [Fact]
    public async Task PutDeviceTrust_rejects_domain_join_requirement_without_allowed_domains()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/device-trust",
            new
            {
                enabled = true,
                requiredCheckinDays = 3,
                requireDomainJoined = true,
                allowedAdDomains = Array.Empty<string>()
            });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("allowed_ad_domains_required");
    }

    [Fact]
    public async Task DeviceTrust_denies_access_when_device_is_not_domain_joined()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: false,
            domainName: "",
            windowsUser: "WORKGROUP\\alice");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("device_not_domain_joined");
    }

    [Fact]
    public async Task DeviceTrust_denies_policy_decision_when_device_id_is_empty()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId,
            deviceId = Guid.Empty,
            requestedPermission = "View"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PolicyDecisionRow>();
        body!.Allowed.Should().BeFalse();
        body.ReasonCode.Should().Be("device_trust_required");
    }

    [Fact]
    public async Task DeviceTrust_denies_unwrap_when_device_id_is_empty()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId,
            deviceId = Guid.Empty,
            requestedPermission = "View"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("device_trust_required");
    }

    [Fact]
    public async Task DeviceTrust_requires_signed_unwrap_for_provisioned_device()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "CORP\\alice",
            deviceSecret: deviceSecret);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
            {
                TenantId = tenantId,
                Enabled = true,
                RequiredCheckinDays = 7,
                RequireDomainJoined = true,
                AllowedAdDomainsCsv = "CORP",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var unsigned = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId,
            deviceId,
            requestedPermission = "View"
        });

        unsigned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unsignedBody = await unsigned.Content.ReadAsStringAsync();
        unsignedBody.Should().Contain("device_signature_required");

        var wrongSignature = DeviceRequestSigning.Sign(
            "wrong-secret",
            DeviceRequestSigning.UnwrapPayload(
                tenantId,
                fileId,
                userId,
                deviceId,
                "View"));

        using var forged = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId,
            deviceId,
            requestedPermission = "View",
            deviceSignature = wrongSignature
        });

        forged.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var forgedBody = await forged.Content.ReadAsStringAsync();
        forgedBody.Should().Contain("device_signature_invalid");
    }

    [Fact]
    public async Task DeviceTrust_denies_access_when_domain_is_not_allowed()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "VENDOR",
            windowsUser: "VENDOR\\alice");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("ad_domain_not_allowed");
    }

    [Fact]
    public async Task DeviceTrust_denies_access_when_windows_user_domain_is_not_allowed()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "WORKSTATION\\alice");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("ad_user_not_allowed");
    }

    [Fact]
    public async Task DeviceTrust_denies_access_when_windows_user_does_not_match_requested_user()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "CORP\\bob");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("ad_user_mismatch");
    }

    [Fact]
    public async Task DeviceTrust_denies_unwrap_when_signed_device_user_differs_from_request_user()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var otherUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            otherUserId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "CORP\\alice",
            deviceSecret: deviceSecret);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
            {
                TenantId = tenantId,
                Enabled = true,
                RequiredCheckinDays = 7,
                RequireDomainJoined = true,
                AllowedAdDomainsCsv = "CORP",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var signature = DeviceRequestSigning.Sign(
            deviceSecret,
            DeviceRequestSigning.UnwrapPayload(
                tenantId,
                fileId,
                userId,
                deviceId,
                "View"));

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId,
            userId,
            deviceId,
            requestedPermission = "View",
            deviceSignature = signature
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("device_user_mismatch");
    }

    [Fact]
    public async Task DeviceTrust_denies_access_when_domain_join_required_without_allowed_domains()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "CORP\\alice");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("allowed_ad_domains_required");
    }

    [Fact]
    public async Task DeviceTrust_legacy_heartbeat_without_posture_clears_domain_trust()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "CORP\\alice");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
            {
                TenantId = tenantId,
                Enabled = true,
                RequiredCheckinDays = 7,
                RequireDomainJoined = true,
                AllowedAdDomainsCsv = "CORP",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var heartbeat = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "legacy"
        });
        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("device_not_domain_joined");
    }

    [Fact]
    public async Task DeviceTrust_allows_access_when_domain_joined_and_domain_allowed()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        await SeedDeviceAsync(
            tenantId,
            deviceId,
            userId,
            lastHeartbeat: DateTimeOffset.UtcNow,
            domainJoined: true,
            domainName: "CORP",
            windowsUser: "CORP\\alice");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            RequireDomainJoined = true,
            AllowedAdDomainsCsv = "CORP,ENGINEERING",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task DeviceTrust_denies_access_when_heartbeat_is_stale()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        // Device last checked in 10 days ago
        await SeedDeviceAsync(tenantId, deviceId, userId,
            lastHeartbeat: DateTimeOffset.UtcNow.AddDays(-10));

        // Enable device trust with 7-day window
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Use simulator — avoids AES-GCM on the allowed path; device trust runs first
        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeFalse();
        result.ReasonCode.Should().Be("device_trust_expired");
    }

    [Fact]
    public async Task DeviceTrust_allows_access_when_heartbeat_is_fresh()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var deviceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

        // Device checked in 2 days ago
        await SeedDeviceAsync(tenantId, deviceId, userId,
            lastHeartbeat: DateTimeOffset.UtcNow.AddDays(-2));

        // Enable device trust with 7-day window
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
        {
            TenantId = tenantId,
            Enabled = true,
            RequiredCheckinDays = 7,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
        result!.Allowed.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Time-windowed grants
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertGrant_stores_time_window()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileId = Guid.NewGuid();
        db.ProtectedFiles.Add(new ProtectedFileEntity
        {
            Id = fileId, TenantId = tenantId, OwnerUserId = userId,
            ContentType = "application/pdf",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            Revoked = false, Permissions = Domain.Permission.None,
            WatermarkTemplate = "", OfflineLeaseMinutes = 0
        });
        await db.SaveChangesAsync();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var until = DateTimeOffset.UtcNow.AddDays(7);

        var res = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            subjectType = "User",
            subjectId = userId,
            permissions = "View",
            validFromUtc = from,
            validUntilUtc = until
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<GrantRow>();
        body!.ValidFromUtc.Should().NotBeNull();
        body.ValidUntilUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Grant_denies_access_when_window_has_not_started()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var ownerId = Guid.NewGuid(); // different owner — prevents owner fallback
        // Grant that starts 1 day in the future
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId,
            validFromUtc: DateTimeOffset.UtcNow.AddDays(1),
            validUntilUtc: DateTimeOffset.UtcNow.AddDays(8));

        var result = await SimulateAsync(tenantId, fileId, userId, Guid.Empty);
        result!.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Grant_denies_access_when_window_has_expired()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var ownerId = Guid.NewGuid(); // different owner — prevents owner fallback
        // Grant that expired 1 day ago
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId,
            validFromUtc: DateTimeOffset.UtcNow.AddDays(-10),
            validUntilUtc: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await SimulateAsync(tenantId, fileId, userId, Guid.Empty);
        result!.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Grant_allows_access_when_within_window()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var ownerId = Guid.NewGuid();
        // Grant that started 1 day ago and expires in 7 days
        var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId,
            validFromUtc: DateTimeOffset.UtcNow.AddDays(-1),
            validUntilUtc: DateTimeOffset.UtcNow.AddDays(7));

        var result = await SimulateAsync(tenantId, fileId, userId, Guid.Empty);
        result!.Allowed.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────

    private sealed record IpRuleRow(
        Guid RuleId, Guid TenantId, string Cidr, string Label, DateTimeOffset CreatedAtUtc);

    private sealed record DeviceTrustRow(
        Guid TenantId,
        bool Enabled,
        int RequiredCheckinDays,
        bool RequireDomainJoined,
        IReadOnlyList<string> AllowedAdDomains,
        DateTimeOffset? UpdatedAtUtc);

    private sealed record GrantRow(
        Guid TenantId, Guid FileId, string SubjectType, Guid SubjectId,
        string Permissions, DateTimeOffset? ValidFromUtc, DateTimeOffset? ValidUntilUtc);

    private sealed record SimulatorResult(
        Guid TenantId, Guid FileId, Guid UserId, bool Allowed,
        string ReasonCode, bool Simulated);

    private sealed record PolicyDecisionRow(
        bool Allowed,
        string AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);
}
