using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ScimUsersApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-scim-users-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    private readonly Guid tenantId = Guid.NewGuid();

    public ScimUsersApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", "test-scim-key");
            });
        client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-scim-key");
    }

    [Fact]
    public async Task CreateUser_returns_201_with_scim_user()
    {
        var response = await client.PostAsync($"/scim/v2/{tenantId}/Users", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = "alice@corp.com",
            displayName = "Alice Smith",
            active = true
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["id"].Should().NotBeNull();
        json["userName"]!.GetValue<string>().Should().Be("alice@corp.com");
        json["displayName"]!.GetValue<string>().Should().Be("Alice Smith");
        json["active"]!.GetValue<bool>().Should().BeTrue();
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUser_returns_200_with_user()
    {
        var userId = await CreateUserAndGetId("bob@corp.com", "Bob Jones");

        var response = await client.GetAsync($"/scim/v2/{tenantId}/Users/{userId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["id"]!.GetValue<string>().Should().Be(userId);
        json["userName"]!.GetValue<string>().Should().Be("bob@corp.com");
    }

    [Fact]
    public async Task GetUser_returns_404_for_unknown_id()
    {
        var response = await client.GetAsync($"/scim/v2/{tenantId}/Users/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListUsers_returns_all_tenant_users()
    {
        var t = Guid.NewGuid();
        var c = AuthClient();
        await PostUser(c, t, "u1@corp.com", "U1");
        await PostUser(c, t, "u2@corp.com", "U2");

        var response = await c.GetAsync($"/scim/v2/{t}/Users");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["totalResults"]!.GetValue<int>().Should().Be(2);
        json["Resources"]!.AsArray().Count.Should().Be(2);
    }

    [Fact]
    public async Task ListUsers_filter_by_userName_returns_matching_user()
    {
        var t = Guid.NewGuid();
        var c = AuthClient();
        await PostUser(c, t, "carol@corp.com", "Carol");
        await PostUser(c, t, "dave@corp.com", "Dave");

        var response = await c.GetAsync($"/scim/v2/{t}/Users?filter=userName eq \"carol@corp.com\"");
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["totalResults"]!.GetValue<int>().Should().Be(1);
        json["Resources"]!.AsArray()[0]!["userName"]!.GetValue<string>().Should().Be("carol@corp.com");
    }

    [Fact]
    public async Task ListUsers_filter_by_externalId_returns_matching_user()
    {
        var t = Guid.NewGuid();
        var c = AuthClient();
        await c.PostAsync($"/scim/v2/{t}/Users", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            externalId = "okta-00u1234",
            userName = "grace@corp.com",
            displayName = "Grace"
        }));

        var response = await c.GetAsync($"/scim/v2/{t}/Users?filter=externalId eq \"okta-00u1234\"");
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["totalResults"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task UpdateUser_put_updates_displayName_and_active()
    {
        var userId = await CreateUserAndGetId("eve@corp.com", "Eve");

        var response = await client.PutAsync($"/scim/v2/{tenantId}/Users/{userId}", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = "eve@corp.com",
            displayName = "Eve Updated",
            active = false
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["displayName"]!.GetValue<string>().Should().Be("Eve Updated");
        json["active"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUser_returns_204_and_user_is_gone()
    {
        var userId = await CreateUserAndGetId("frank@corp.com", "Frank");

        var deleteResponse = await client.DeleteAsync($"/scim/v2/{tenantId}/Users/{userId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/scim/v2/{tenantId}/Users/{userId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Request_without_auth_returns_401()
    {
        var noAuth = factory.CreateClient();
        var response = await noAuth.GetAsync($"/scim/v2/{tenantId}/Users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var f in new[] { path, $"{path}-wal", $"{path}-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private HttpClient AuthClient()
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("Authorization", "Bearer test-scim-key");
        return c;
    }

    private async Task<string> CreateUserAndGetId(string email, string displayName)
    {
        var response = await PostUser(client, tenantId, email, displayName);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        return json!["id"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> PostUser(HttpClient c, Guid tid, string email, string displayName)
        => await c.PostAsync($"/scim/v2/{tid}/Users", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = email,
            displayName
        }));
}
