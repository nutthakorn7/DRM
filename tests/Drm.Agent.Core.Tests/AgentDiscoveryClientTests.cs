using System.Net;
using System.Text;
using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

/// <summary>
/// Tests for DrmServerClient.DiscoverAsync — the wire-up for the Stage
/// 3 first-run flow. Server-side semantics are already covered by
/// AgentDiscoverApiTests in Drm.Server.Tests; these tests focus on
/// the client's contract: parsing the JSON, mapping 404 to null,
/// URL-escaping the email correctly, and propagating other failures.
/// </summary>
public sealed class AgentDiscoveryClientTests
{
    [Fact]
    public async Task DiscoverAsync_returns_typed_result_on_200()
    {
        HttpRequestMessage? captured = null;
        var handler = new DiscoverStubHandler(request =>
        {
            captured = request;
            var json = """
                {
                    "tenantId": "a1f7b1c2-d3e4-4f56-9a78-0b1c2d3e4f56",
                    "userId":   "b2a8c2d3-e4f5-4a67-8b89-1c2d3e4f5a67",
                    "displayName": "Alice Tester",
                    "email":       "alice@acme.test",
                    "defaultPolicyTemplateId": "c3b9d3e4-f5a6-4b78-9c9a-2d3e4f5a6b78",
                    "defaultExpiryDays": 7
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

        var result = await client.DiscoverAsync("alice@acme.test", CancellationToken.None);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(Guid.Parse("a1f7b1c2-d3e4-4f56-9a78-0b1c2d3e4f56"));
        result.UserId.Should().Be(Guid.Parse("b2a8c2d3-e4f5-4a67-8b89-1c2d3e4f5a67"));
        result.DisplayName.Should().Be("Alice Tester");
        result.Email.Should().Be("alice@acme.test");
        result.DefaultPolicyTemplateId.Should().Be(Guid.Parse("c3b9d3e4-f5a6-4b78-9c9a-2d3e4f5a6b78"));
        result.DefaultExpiryDays.Should().Be(7);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        // The relative URL the client built should target the
        // discover endpoint with the email passed through
        // Uri.EscapeDataString.
        captured.RequestUri!.AbsolutePath.Should().Be("/api/agent/discover");
        captured.RequestUri.Query.Should().Be("?email=alice%40acme.test");
    }

    [Fact]
    public async Task DiscoverAsync_returns_null_on_404()
    {
        var handler = new DiscoverStubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        var result = await client.DiscoverAsync("nobody@nowhere.test", CancellationToken.None);

        result.Should().BeNull(
            "404 is 'unknown email' — a normal first-run flow, not an exceptional condition");
    }

    [Fact]
    public async Task DiscoverAsync_propagates_other_http_failures()
    {
        var handler = new DiscoverStubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        var act = async () => await client.DiscoverAsync("alice@acme.test", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>(
            "5xx is a server-side bug; bubble it up so the user sees a real error");
    }

    [Fact]
    public async Task DiscoverAsync_url_escapes_special_email_characters()
    {
        HttpRequestMessage? captured = null;
        var handler = new DiscoverStubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        // '+' is valid in email local-parts (gmail aliases) but means
        // "space" in form-encoded URLs. Uri.EscapeDataString must
        // produce %2B for it; otherwise the server sees a space.
        await client.DiscoverAsync("alice+tag@acme.test", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RequestUri!.Query.Should().Contain("alice%2Btag%40acme.test");
    }

    [Fact]
    public async Task DiscoverAsync_rejects_empty_email_before_calling_the_server()
    {
        var handler = new DiscoverStubHandler(_ =>
        {
            throw new InvalidOperationException("Server should not be called for empty email");
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });

        var act = async () => await client.DiscoverAsync(" ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class DiscoverStubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}
