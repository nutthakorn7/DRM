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

        try
        {
            using var http = httpClientFactory.CreateClient("EntraGraph");
            var token = await AcquireTokenAsync(http, config.EntraTenantId, config.ClientId, config.ClientSecret, cancellationToken);

            var entraUsers = await FetchAllPagesAsync<EntraUser>(
                http, "https://graph.microsoft.com/v1.0/users?$select=id,mail,displayName,userPrincipalName",
                token, cancellationToken);
            var entraGroups = await FetchAllPagesAsync<EntraGroup>(
                http, "https://graph.microsoft.com/v1.0/groups?$select=id,displayName",
                token, cancellationToken);

            var users = entraUsers
                .Where(u => !string.IsNullOrWhiteSpace(u.Id) && !string.IsNullOrWhiteSpace(u.Mail ?? u.UserPrincipalName))
                .Select(u => new DirectoryUser(u.Id, (u.Mail ?? u.UserPrincipalName)!, u.DisplayName ?? (u.Mail ?? u.UserPrincipalName)!))
                .ToList();
            var groups = entraGroups
                .Where(g => !string.IsNullOrWhiteSpace(g.Id))
                .Select(g => new DirectoryGroup(g.Id, g.DisplayName ?? g.Id))
                .ToList();

            var memberships = new List<DirectoryMembership>();
            foreach (var g in groups)
            {
                var members = await FetchAllPagesAsync<EntraDirectoryObject>(
                    http, $"https://graph.microsoft.com/v1.0/groups/{g.ExternalId}/members?$select=id",
                    token, cancellationToken);
                foreach (var m in members)
                    if (!string.IsNullOrWhiteSpace(m.Id))
                        memberships.Add(new DirectoryMembership(g.ExternalId, m.Id));
            }

            // Reaching here means every page of every fetch succeeded → a complete sync, so it is
            // safe to reconcile removals (gated per-tenant by config). A failed/partial fetch throws
            // above and lands in catch, where nothing is deactivated.
            var result = await DirectoryUpsert.ApplyAsync(
                dbContext, tenantId, users, groups, memberships,
                reconcileRemovals: config.ReconcileRemovals, cancellationToken);

            var syncConfig = await dbContext.TenantDirectorySyncConfigs
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
            if (syncConfig != null)
            {
                syncConfig.LastSyncAtUtc = DateTimeOffset.UtcNow;
                syncConfig.LastSyncStatus = "ok";
                syncConfig.LastSyncUserCount = result.UsersUpserted;
                syncConfig.LastSyncGroupCount = result.GroupsUpserted;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation(
                "Entra sync complete: {Users} users, {Groups} groups, {Memberships} memberships upserted; {Deact} deactivated, {Pruned} memberships pruned.{Warn}",
                result.UsersUpserted, result.GroupsUpserted, result.MembershipsUpserted,
                result.UsersDeactivated, result.MembershipsRemoved,
                result.Warnings.Count > 0 ? " " + string.Join("; ", result.Warnings) : "");

            return new DirectorySyncResult(result.UsersUpserted, result.GroupsUpserted, result.MembershipsUpserted);
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
