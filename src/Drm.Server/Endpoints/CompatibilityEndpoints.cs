using Microsoft.AspNetCore.Http.HttpResults;

namespace Drm.Server.Endpoints;

public static class CompatibilityEndpoints
{
    public static IEndpointRouteBuilder MapCompatibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/compatibility-matrix", GetMatrix);
        return endpoints;
    }

    private static Ok<MatrixResponse> GetMatrix()
    {
        return TypedResults.Ok(new MatrixResponse(
            CompatibilityMatrix.Categories,
            CompatibilityMatrix.KnownIssues.Count,
            DateTimeOffset.UtcNow));
    }

    private sealed record MatrixResponse(
        IReadOnlyList<CompatibilityCategory> Categories,
        int KnownIssueCount,
        DateTimeOffset GeneratedAtUtc);
}
