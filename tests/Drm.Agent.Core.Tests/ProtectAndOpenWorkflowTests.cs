using System.Net;
using System.Text;
using System.Text.Json;
using Drm.Agent.Core;
using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class ProtectAndOpenWorkflowTests
{
    [Fact]
    public async Task OpenProtectedFileWorkflow_byte_array_open_returns_header_content_type()
    {
        var server = new FakeDrmServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var fileId = ProtectedFileId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        using var output = new MemoryStream();
        ProtectedFileWriter.Write(
            output,
            tenantId,
            fileId,
            "text/csv",
            fileKey,
            "a,b"u8.ToArray());

        await server.RegisterFileAsync(
            tenantId.Value,
            fileId.Value,
            userId.Value,
            "text/csv",
            DateTimeOffset.UtcNow.AddHours(1),
            Permission.View,
            CancellationToken.None);

        var opened = await new OpenProtectedFileWorkflow(server)
            .OpenAsync(output.ToArray(), userId, deviceId, fileKey, CancellationToken.None);

        opened.ContentType.Should().Be("text/csv");
        opened.Content.Should().Equal("a,b"u8.ToArray());
    }

    [Fact]
    public async Task Protect_registers_file_and_open_decrypts_when_policy_allows()
    {
        var server = new FakeDrmServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var pdf = "%PDF-1.7"u8.ToArray();
        var fileKey = EnvelopeCrypto.GenerateKey();

        var protect = new ProtectPdfWorkflow(server);
        var protectedBytes = await protect.ProtectAsync(tenantId, userId, pdf, fileKey, CancellationToken.None);

        var open = new OpenProtectedPdfWorkflow(server);
        var opened = await open.OpenAsync(protectedBytes, userId, deviceId, fileKey, CancellationToken.None);

        opened.Content.Should().Equal(pdf);
        opened.Watermark.Should().Contain(userId.Value.ToString("N"));
    }

    [Fact]
    public async Task Open_throws_when_policy_denies()
    {
        var server = new FakeDrmServerClient();
        var tenantId = TenantId.New();
        var ownerUserId = UserId.New();
        var deniedUserId = UserId.New();
        var deviceId = DeviceId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var protectedBytes = await new ProtectPdfWorkflow(server)
            .ProtectAsync(tenantId, ownerUserId, "%PDF-1.7"u8.ToArray(), fileKey, CancellationToken.None);

        var open = new OpenProtectedPdfWorkflow(server);
        var act = () => open.OpenAsync(protectedBytes, deniedUserId, deviceId, fileKey, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access denied: denied");
    }

    [Fact]
    public async Task Open_stores_allowed_online_decision_in_policy_cache()
    {
        var server = new FakeDrmServerClient();
        var cache = new RecordingPolicyDecisionCache();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var protectedBytes = await new ProtectPdfWorkflow(server)
            .ProtectAsync(tenantId, userId, "%PDF-1.7"u8.ToArray(), fileKey, CancellationToken.None);

        await new OpenProtectedPdfWorkflow(server, cache)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, CancellationToken.None);

        cache.StoredEntries.Should().ContainSingle(entry =>
            entry.Key.TenantId == tenantId.Value &&
            entry.Key.UserId == userId.Value &&
            entry.Key.DeviceId == deviceId.Value &&
            entry.Key.RequestedPermission == Permission.View &&
            entry.AllowedPermissions == Permission.View &&
            entry.OfflineLeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Open_uses_cached_decision_when_server_is_unavailable()
    {
        var tenantId = TenantId.New();
        var ownerUserId = UserId.New();
        var deviceId = DeviceId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var protectedBytes = await new ProtectPdfWorkflow(new FakeDrmServerClient())
            .ProtectAsync(tenantId, ownerUserId, "%PDF-1.7 offline"u8.ToArray(), fileKey, CancellationToken.None);
        var package = Drm.Container.ProtectedFileReader.Read(new MemoryStream(protectedBytes, writable: false));
        var cache = new RecordingPolicyDecisionCache
        {
            CachedEntry = new CachedPolicyDecision(
                new PolicyDecisionCacheKey(
                    tenantId.Value,
                    package.Header.FileId,
                    ownerUserId.Value,
                    deviceId.Value,
                    Permission.View),
                "{user} offline",
                Permission.View,
                DateTimeOffset.UtcNow.AddMinutes(5))
        };

        var opened = await new OpenProtectedPdfWorkflow(new OfflineDrmServerClient(), cache)
            .OpenAsync(protectedBytes, ownerUserId, deviceId, fileKey, CancellationToken.None);

        opened.Content.Should().Equal("%PDF-1.7 offline"u8.ToArray());
        opened.Watermark.Should().Contain("offline");
        cache.LookupKeys.Should().ContainSingle(key =>
            key.FileId == package.Header.FileId &&
            key.UserId == ownerUserId.Value &&
            key.DeviceId == deviceId.Value &&
            key.RequestedPermission == Permission.View);
    }

    [Fact]
    public async Task Open_denies_offline_when_cached_decision_is_missing()
    {
        var tenantId = TenantId.New();
        var ownerUserId = UserId.New();
        var deviceId = DeviceId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var protectedBytes = await new ProtectPdfWorkflow(new FakeDrmServerClient())
            .ProtectAsync(tenantId, ownerUserId, "%PDF-1.7"u8.ToArray(), fileKey, CancellationToken.None);

        var act = () => new OpenProtectedPdfWorkflow(new OfflineDrmServerClient(), new RecordingPolicyDecisionCache())
            .OpenAsync(protectedBytes, ownerUserId, deviceId, fileKey, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access denied: offline_lease_missing");
    }

    [Fact]
    public async Task DrmServerClient_posts_decision_request_and_parses_allowed_permissions()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;

            var json = """
                {
                  "allowed": true,
                  "allowedPermissions": "View, Print",
                  "reasonCode": "allowed",
                  "watermarkTemplate": "{user} {file}",
                  "offlineLeaseExpiresAtUtc": "2026-05-15T01:00:00Z"
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        var decision = await client.DecideAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Permission.View,
            CancellationToken.None);

        decision.Allowed.Should().BeTrue();
        decision.AllowedPermissions.Should().Be(Permission.View | Permission.Print);
        decision.ReasonCode.Should().Be("allowed");
        decision.WatermarkTemplate.Should().Be("{user} {file}");
        decision.OfflineLeaseExpiresAtUtc.Should().Be(DateTimeOffset.Parse("2026-05-15T01:00:00Z"));
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri("https://drm.example/api/policy/decide"));

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("requestedPermission").GetString().Should().Be("View");
    }

    [Fact]
    public async Task DrmServerClient_treats_blank_allowed_permissions_as_none()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"allowed":false,"allowedPermissions":"","reasonCode":"denied","watermarkTemplate":null}""",
                Encoding.UTF8,
                "application/json")
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        var decision = await client.DecideAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Permission.View,
            CancellationToken.None);

        decision.AllowedPermissions.Should().Be(Permission.None);
    }

    [Fact]
    public async Task DrmServerClient_rejects_undefined_allowed_permission_bits()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"allowed":true,"allowedPermissions":"64","reasonCode":"allowed","watermarkTemplate":null}""",
                Encoding.UTF8,
                "application/json")
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        var act = () => client.DecideAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Permission.View,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Policy decision returned invalid permissions '64'.");
    }

    private sealed class FakeDrmServerClient : IDrmServerClient
    {
        private Guid _tenantId;
        private Guid _fileId;
        private Guid _ownerUserId;

        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            _tenantId = tenantId;
            _fileId = fileId;
            _ownerUserId = ownerUserId;
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            if (tenantId == _tenantId && fileId == _fileId && userId == _ownerUserId && permission == Permission.View)
            {
                return Task.FromResult(new OpenDecision(true, "allowed", "{user} {file}", Permission.View, DateTimeOffset.UtcNow.AddMinutes(5)));
            }

            return Task.FromResult(new OpenDecision(false, "denied", null, Permission.None, null));
        }

        public Task<AgentDeviceRegistration> RegisterDeviceAsync(AgentIdentity identity, string hostname, string operatingSystem, string agentVersion, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentDeviceRegistration(
                identity.TenantId,
                identity.UserId,
                identity.DeviceId,
                hostname,
                operatingSystem,
                agentVersion,
                "registered",
                DateTimeOffset.UtcNow,
                null));
        }

        public Task<AgentHeartbeat> RecordHeartbeatAsync(AgentIdentity identity, string status, string agentVersion, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentHeartbeat(identity.DeviceId, status, DateTimeOffset.UtcNow));
        }

        public Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(AgentIdentity identity, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AgentCommand>>([]);
        }

        public Task<AgentCommand> CompleteCommandAsync(AgentIdentity identity, Guid commandId, AgentCommandCompletion completion, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task WrapFileKeyAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<UnwrappedFileKey> UnwrapFileKeyAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, string requestedPermission, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class OfflineDrmServerClient : IDrmServerClient
    {
        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task<AgentDeviceRegistration> RegisterDeviceAsync(AgentIdentity identity, string hostname, string operatingSystem, string agentVersion, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task<AgentHeartbeat> RecordHeartbeatAsync(AgentIdentity identity, string status, string agentVersion, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(AgentIdentity identity, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task<AgentCommand> CompleteCommandAsync(AgentIdentity identity, Guid commandId, AgentCommandCompletion completion, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task WrapFileKeyAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }

        public Task<UnwrappedFileKey> UnwrapFileKeyAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, string requestedPermission, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("server unavailable");
        }
    }

    private sealed class RecordingPolicyDecisionCache : IPolicyDecisionCache
    {
        public CachedPolicyDecision? CachedEntry { get; init; }

        public List<CachedPolicyDecision> StoredEntries { get; } = [];

        public List<PolicyDecisionCacheKey> LookupKeys { get; } = [];

        public Task StoreAsync(CachedPolicyDecision decision, CancellationToken cancellationToken)
        {
            StoredEntries.Add(decision);
            return Task.CompletedTask;
        }

        public Task<CachedPolicyDecision?> TryGetAllowedAsync(PolicyDecisionCacheKey key, DateTimeOffset atUtc, CancellationToken cancellationToken)
        {
            LookupKeys.Add(key);
            if (CachedEntry is null || CachedEntry.OfflineLeaseExpiresAtUtc <= atUtc)
            {
                return Task.FromResult<CachedPolicyDecision?>(null);
            }

            return Task.FromResult<CachedPolicyDecision?>(CachedEntry);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}
