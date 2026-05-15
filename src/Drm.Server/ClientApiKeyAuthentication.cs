using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Drm.Server;

public static class ClientApiKeyAuthentication
{
    public const string HeaderName = "X-DRM-Client-Key";

    public static IApplicationBuilder UseClientApiKeyAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/share-links", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            var configuredKey = configuration["Drm:Security:ClientApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                await next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var submittedKey) ||
                StringValues.IsNullOrEmpty(submittedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("client_api_key_required"));
                return;
            }

            if (!KeysMatch(configuredKey, submittedKey.ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("client_api_key_invalid"));
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
