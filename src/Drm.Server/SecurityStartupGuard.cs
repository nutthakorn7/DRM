namespace Drm.Server;

/// <summary>
/// Validates security-relevant configuration at startup. In non-Development
/// environments the server refuses to run with placeholder defaults so that
/// a fresh deployment cannot silently ship with a known HMAC key or an
/// unauthenticated admin surface.
/// </summary>
public static class SecurityStartupGuard
{
    public const string DefaultTrailerSecretPlaceholder = "drm-transparent-default-secret";

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var adminKey = configuration["Drm:Security:AdminApiKey"];
        if (string.IsNullOrWhiteSpace(adminKey))
        {
            throw new InvalidOperationException(
                "Drm:Security:AdminApiKey must be set in non-Development environments. " +
                "Set it via environment variable or appsettings.{Environment}.json before starting the server.");
        }

        var trailerSecret = configuration["Drm:Security:TransparentTrailerSecret"];
        if (string.IsNullOrWhiteSpace(trailerSecret))
        {
            throw new InvalidOperationException(
                "Drm:Security:TransparentTrailerSecret must be set in non-Development environments. " +
                "Generate with `openssl rand -hex 32` and store it as a secret.");
        }
        if (string.Equals(trailerSecret, DefaultTrailerSecretPlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Drm:Security:TransparentTrailerSecret is set to the documented placeholder. " +
                "Generate a real secret with `openssl rand -hex 32` before deploying.");
        }
    }
}
