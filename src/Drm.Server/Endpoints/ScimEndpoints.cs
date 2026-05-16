namespace Drm.Server.Endpoints;

public static class ScimEndpoints
{
    public static IEndpointRouteBuilder MapScimEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/scim/v2/{tenantId:guid}/ServiceProviderConfig", (Guid tenantId) =>
        {
            var config = new
            {
                schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
                documentationUri = string.Empty,
                patch = new { supported = false },
                bulk = new { supported = false, maxOperations = 0, maxPayloadSize = 0 },
                filter = new { supported = true, maxResults = 200 },
                changePassword = new { supported = false },
                sort = new { supported = false },
                etag = new { supported = false },
                authenticationSchemes = new[]
                {
                    new
                    {
                        type = "oauthbearertoken",
                        name = "OAuth Bearer Token",
                        description = "Authentication using an OAuth Bearer Token",
                        specUri = "http://www.rfc-editor.org/info/rfc6750",
                        primary = true
                    }
                },
                meta = new
                {
                    resourceType = "ServiceProviderConfig",
                    location = $"/scim/v2/{tenantId}/ServiceProviderConfig"
                }
            };
            return Results.Json(config, contentType: "application/scim+json");
        });

        return endpoints;
    }
}
