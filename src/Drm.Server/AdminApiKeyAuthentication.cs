using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Drm.Server;

public static class AdminApiKeyAuthentication
{
    public const string HeaderName = "X-DRM-Admin-Key";

    public static IApplicationBuilder UseAdminApiKeyAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
            var configuredKey = configuration["Drm:Security:AdminApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                // SecurityStartupGuard refuses to start in non-Development with
                // a blank admin key. This branch only runs under Development.
                if (!environment.IsDevelopment())
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse("admin_api_key_unconfigured"));
                    return;
                }

                await next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var submittedKey) ||
                StringValues.IsNullOrEmpty(submittedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("admin_api_key_required"));
                return;
            }

            if (!KeysMatch(configuredKey, submittedKey.ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("admin_api_key_invalid"));
                return;
            }

            await next(context);
        });
    }

    private static bool KeysMatch(string configuredKey, string submittedKey)
    {
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var submittedBytes = Encoding.UTF8.GetBytes(submittedKey);
        return configuredBytes.Length == submittedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(configuredBytes, submittedBytes);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
