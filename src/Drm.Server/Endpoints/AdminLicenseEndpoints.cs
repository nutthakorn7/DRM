using Microsoft.AspNetCore.Http.HttpResults;

namespace Drm.Server.Endpoints;

public static class AdminLicenseEndpoints
{
    public static IEndpointRouteBuilder MapAdminLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/license", GetLicense);
        return endpoints;
    }

    private static IResult GetLicense(HttpContext httpContext, IConfiguration configuration)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        var configured = configuration["Drm:License:EnabledTiers"];
        var paidEncrypters = int.TryParse(configuration["Drm:License:PaidEncrypterCount"], out var p) ? p : 0;
        var tier = LicenseTierParser.ParseConfigured(configured);

        var tiers = new List<string>();
        foreach (var value in Enum.GetValues<LicenseTier>())
        {
            if (value == LicenseTier.None || value == LicenseTier.All) continue;
            if (tier.HasFlag(value)) tiers.Add(value.ToString());
        }

        return Results.Ok(new LicenseResponse(
            tiers,
            paidEncrypters,
            paidEncrypters * 9));
    }

    private sealed record LicenseResponse(
        IReadOnlyList<string> EnabledTiers,
        int PaidEncrypterCount,
        int FreeViewerCount);
}
