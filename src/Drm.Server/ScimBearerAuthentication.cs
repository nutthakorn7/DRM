using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Drm.Server;

public static class ScimBearerAuthentication
{
    public static IApplicationBuilder UseScimBearerAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/scim/v2", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            var configuredKey = configuration["Drm:Security:AdminApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                await next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
                StringValues.IsNullOrEmpty(authHeader) ||
                !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ScimError { Status = "401", Detail = "Bearer token required." }, options: null, contentType: "application/scim+json");
                return;
            }

            var submitted = authHeader.ToString()["Bearer ".Length..].Trim();
            if (!KeysMatch(configuredKey, submitted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ScimError { Status = "403", Detail = "Invalid bearer token." }, options: null, contentType: "application/scim+json");
                return;
            }

            await next(context);
        });
    }

    private static bool KeysMatch(string configured, string submitted)
    {
        var a = Encoding.UTF8.GetBytes(configured);
        var b = Encoding.UTF8.GetBytes(submitted);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
