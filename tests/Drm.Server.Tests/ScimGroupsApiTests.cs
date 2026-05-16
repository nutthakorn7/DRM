using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ScimGroupsApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-scim-groups-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    private readonly Guid tenantId = Guid.NewGuid();

    public ScimGroupsApiTests()
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
    public async Task CreateGroup_returns_201_with_scim_group()
    {
        var response = await client.PostAsync($"/scim/v2/{tenantId}/Groups", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = "Engineering"
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["id"].Should().NotBeNull();
        json["displayName"]!.GetValue<string>().Should().Be("Engineering");
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task GetGroup_returns_200_with_group()
    {
        var groupId = await CreateGroupAndGetId("Design");

        var response = await client.GetAsync($"/scim/v2/{tenantId}/Groups/{groupId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["displayName"]!.GetValue<string>().Should().Be("Design");
    }

    [Fact]
    public async Task GetGroup_returns_404_for_unknown_id()
    {
        var response = await client.GetAsync($"/scim/v2/{tenantId}/Groups/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListGroups_returns_all_tenant_groups()
    {
        var t = Guid.NewGuid();
        var c = AuthClient();
        await c.PostAsync($"/scim/v2/{t}/Groups", JsonContent.Create(new { displayName = "A" }));
        await c.PostAsync($"/scim/v2/{t}/Groups", JsonContent.Create(new { displayName = "B" }));

        var response = await c.GetAsync($"/scim/v2/{t}/Groups");
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        json!["totalResults"]!.GetValue<int>().Should().Be(2);
        json["Resources"]!.AsArray().Count.Should().Be(2);
    }

    [Fact]
    public async Task UpdateGroup_put_replaces_members()
    {
        // Create a user
        var userResponse = await client.PostAsync($"/scim/v2/{tenantId}/Users", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = "member@corp.com",
            displayName = "Member"
        }));
        var userId = (await userResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

        var groupId = await CreateGroupAndGetId("Team");

        // Add member via PUT
        var putResponse = await client.PutAsync($"/scim/v2/{tenantId}/Groups/{groupId}", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = "Team",
            members = new[] { new { value = userId } }
        }));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await putResponse.Content.ReadFromJsonAsync<JsonObject>();
        json!["members"]!.AsArray().Count.Should().Be(1);
        json["members"]!.AsArray()[0]!["value"]!.GetValue<string>().Should().Be(userId);

        // Replace with empty member list
        var emptyPut = await client.PutAsync($"/scim/v2/{tenantId}/Groups/{groupId}", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = "Team",
            members = Array.Empty<object>()
        }));
        var emptyJson = await emptyPut.Content.ReadFromJsonAsync<JsonObject>();
        emptyJson!["members"]!.AsArray().Count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteGroup_returns_204_and_group_is_gone()
    {
        var groupId = await CreateGroupAndGetId("Temp");

        var deleteResponse = await client.DeleteAsync($"/scim/v2/{tenantId}/Groups/{groupId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/scim/v2/{tenantId}/Groups/{groupId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private async Task<string> CreateGroupAndGetId(string displayName)
    {
        var response = await client.PostAsync($"/scim/v2/{tenantId}/Groups", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName
        }));
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
    }
}
