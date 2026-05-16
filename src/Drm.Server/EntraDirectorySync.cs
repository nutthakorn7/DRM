using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public interface IDirectorySyncService
{
    Task<DirectorySyncResult> SyncAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record DirectorySyncResult(int UsersUpserted, int GroupsUpserted, int MembershipsUpserted);

public sealed class EntraIdDirectorySyncService(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    ILogger<EntraIdDirectorySyncService> logger) : IDirectorySyncService
{
    public async Task<DirectorySyncResult> SyncAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var config = await dbContext.TenantDirectorySyncConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Directory sync not configured for this tenant.");

        using var http = httpClientFactory.CreateClient("EntraGraph");
        var token = await AcquireTokenAsync(http, config.EntraTenantId, config.ClientId, config.ClientSecret, cancellationToken);

        var entraUsers = await FetchAllPagesAsync<EntraUser>(
            http, "https://graph.microsoft.com/v1.0/users?$select=id,mail,displayName,userPrincipalName",
            token, cancellationToken);

        var entraGroups = await FetchAllPagesAsync<EntraGroup>(
            http, "https://graph.microsoft.com/v1.0/groups?$select=id,displayName",
            token, cancellationToken);

        int usersUpserted = 0;

        foreach (var u in entraUsers)
        {
            var externalId = u.Id;
            if (string.IsNullOrWhiteSpace(externalId)) continue;

            var email = u.Mail ?? u.UserPrincipalName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) continue;

            var displayName = u.DisplayName ?? email;

            var existing = await dbContext.TenantUsers
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ExternalId == externalId, cancellationToken);

            if (existing == null)
            {
                dbContext.TenantUsers.Add(new TenantUserEntity
                {
                    TenantId = tenantId,
                    UserId = Guid.NewGuid(),
                    Email = email,
                    DisplayName = displayName,
                    ExternalId = externalId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                usersUpserted++;
            }
            else
            {
                existing.Email = email;
                existing.DisplayName = displayName;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        int groupsUpserted = 0;
        int membershipsUpserted = 0;

        var groupsToSync = new List<(string ExternalId, Guid GroupId, string Name)>();

        foreach (var g in entraGroups)
        {
            var externalId = g.Id;
            if (string.IsNullOrWhiteSpace(externalId)) continue;

            var name = g.DisplayName ?? externalId;

            var existingGroup = await dbContext.TenantGroups
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ExternalId == externalId, cancellationToken);

            Guid groupId;
            if (existingGroup == null)
            {
                var newGroup = new TenantGroupEntity
                {
                    TenantId = tenantId,
                    GroupId = Guid.NewGuid(),
                    Name = name,
                    ExternalId = externalId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                dbContext.TenantGroups.Add(newGroup);
                groupId = newGroup.GroupId;
                groupsUpserted++;
            }
            else
            {
                existingGroup.Name = name;
                groupId = existingGroup.GroupId;
            }

            groupsToSync.Add((externalId, groupId, name));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (externalId, groupId, _) in groupsToSync)
        {
            var members = await FetchAllPagesAsync<EntraDirectoryObject>(
                http, $"https://graph.microsoft.com/v1.0/groups/{externalId}/members?$select=id",
                token, cancellationToken);

            foreach (var m in members)
            {
                var memberExternalId = m.Id;
                if (string.IsNullOrWhiteSpace(memberExternalId)) continue;

                var memberUser = await dbContext.TenantUsers
                    .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.ExternalId == memberExternalId, cancellationToken);

                if (memberUser == null) continue;

                var memberExists = await dbContext.GroupMembers.AnyAsync(
                    x => x.TenantId == tenantId && x.GroupId == groupId && x.UserId == memberUser.UserId,
                    cancellationToken);

                if (!memberExists)
                {
                    dbContext.GroupMembers.Add(new GroupMemberEntity
                    {
                        TenantId = tenantId,
                        GroupId = groupId,
                        UserId = memberUser.UserId,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                    membershipsUpserted++;
                }
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            var syncConfig = await dbContext.TenantDirectorySyncConfigs
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

            if (syncConfig != null)
            {
                syncConfig.LastSyncAtUtc = DateTimeOffset.UtcNow;
                syncConfig.LastSyncStatus = "ok";
                syncConfig.LastSyncUserCount = usersUpserted;
                syncConfig.LastSyncGroupCount = groupsUpserted;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation("Entra sync complete: {Users} users, {Groups} groups, {Memberships} memberships upserted.",
                usersUpserted, groupsUpserted, membershipsUpserted);

            return new DirectorySyncResult(usersUpserted, groupsUpserted, membershipsUpserted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Entra sync failed for tenant {TenantId}", tenantId);

            var syncConfig = await dbContext.TenantDirectorySyncConfigs
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

            if (syncConfig != null)
            {
                syncConfig.LastSyncAtUtc = DateTimeOffset.UtcNow;
                syncConfig.LastSyncStatus = "error";
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    private static async Task<string> AcquireTokenAsync(
        HttpClient http, string entraTenantId, string clientId, string clientSecret,
        CancellationToken cancellationToken)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{entraTenantId}/oauth2/v2.0/token";
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "https://graph.microsoft.com/.default"
        });

        using var response = await http.PostAsync(tokenUrl, body, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        return json?.AccessToken ?? throw new InvalidOperationException("No access_token in token response.");
    }

    private static async Task<List<T>> FetchAllPagesAsync<T>(
        HttpClient http, string url, string accessToken, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        string? nextUrl = url;

        while (nextUrl != null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<GraphPage<T>>(cancellationToken: cancellationToken);
            if (page?.Value != null) results.AddRange(page.Value);
            nextUrl = page?.NextLink;
        }

        return results;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class GraphPage<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class EntraUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("mail")]
        public string? Mail { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; set; }
    }

    private sealed class EntraGroup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private sealed class EntraDirectoryObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
