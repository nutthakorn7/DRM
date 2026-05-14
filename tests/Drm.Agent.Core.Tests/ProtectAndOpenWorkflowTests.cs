using System.Net;
using System.Text;
using System.Text.Json;
using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class ProtectAndOpenWorkflowTests
{
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
                  "watermarkTemplate": "{user} {file}"
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
                return Task.FromResult(new OpenDecision(true, "allowed", "{user} {file}", Permission.View));
            }

            return Task.FromResult(new OpenDecision(false, "denied", null, Permission.None));
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
