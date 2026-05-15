# Phase 5A Client API Key Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add optional shared-key authentication for non-admin client APIs.

**Architecture:** Keep admin authentication separate on `/api/admin/*`. Add a `ClientApiKeyAuthentication` middleware that only activates when `Drm:Security:ClientApiKey` is configured and protects `/api/*` except `/api/admin/*`. This keeps existing dev/test behavior unchanged unless the deployment opts in.

**Tech Stack:** ASP.NET Core middleware, .NET 10, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Server/ClientApiKeyAuthentication.cs`: middleware for `X-DRM-Client-Key`.
- Modify `src/Drm.Server/Program.cs`: register client API key middleware after admin API key middleware.
- Create `tests/Drm.Server.Tests/ClientApiKeyAuthenticationTests.cs`: test missing, wrong, matching, admin exclusion, and health exclusion behavior.
- Modify `deploy/management/appsettings.onprem.example.json`: document optional `ClientApiKey`.
- Modify `deploy/management/start-management.sh`: export `Drm__Security__ClientApiKey` when `DRM_CLIENT_API_KEY` is provided.
- Modify `deploy/management/README.md`: document optional client key and required header.
- Modify `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs`: assert install assets include client key config.
- Modify `README.md`: add Phase 5A note.

## Tasks

### Task 1: Server Client API Key Guard

- [x] **Step 1: Write failing auth tests**

Create `tests/Drm.Server.Tests/ClientApiKeyAuthenticationTests.cs` with tests asserting:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ClientApiKeyAuthenticationTests : IDisposable
{
    private const string AdminApiKey = "secret-admin-key";
    private const string ClientApiKey = "secret-client-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-client-auth-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public ClientApiKeyAuthenticationTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Security:ClientApiKey", ClientApiKey);
            });
    }

    [Fact]
    public async Task Client_endpoint_requires_api_key_when_configured()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/audit?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Client_endpoint_rejects_wrong_api_key_when_configured()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Client-Key", "wrong-key");

        using var response = await client.GetAsync($"/api/audit?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Client_endpoint_allows_matching_api_key_when_configured()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);

        using var response = await client.GetAsync($"/api/audit?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_endpoint_still_uses_admin_key_only()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Admin-Key", AdminApiKey);

        using var response = await client.GetAsync($"/api/admin/users?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_endpoint_does_not_require_client_api_key()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
```

- [x] **Step 2: Run failing auth tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ClientApiKeyAuthenticationTests
```

Expected: FAIL because `/api/audit` does not require `X-DRM-Client-Key`.

- [x] **Step 3: Implement middleware**

Add `src/Drm.Server/ClientApiKeyAuthentication.cs`:

```csharp
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
                context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
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
```

Register it in `src/Drm.Server/Program.cs`:

```csharp
app.UseAdminApiKeyAuthentication();
app.UseClientApiKeyAuthentication();
```

- [x] **Step 4: Run passing auth tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ClientApiKeyAuthenticationTests
```

Expected: PASS.

### Task 2: Install Assets and Docs

- [x] **Step 1: Write failing install asset assertions**

Update `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs` to assert:

```csharp
root.GetProperty("Drm")
    .GetProperty("Security")
    .GetProperty("ClientApiKey")
    .GetString()
    .Should()
    .Be("REPLACE_WITH_CLIENT_API_KEY");
script.Should().Contain("DRM_CLIENT_API_KEY");
script.Should().Contain("Drm__Security__ClientApiKey");
```

- [x] **Step 2: Run failing install tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementInstallAssetsTests
```

Expected: FAIL until config/script are updated.

- [x] **Step 3: Update install assets**

Add `ClientApiKey` to `deploy/management/appsettings.onprem.example.json` and optional export logic to `deploy/management/start-management.sh`:

```bash
if [[ -n "${DRM_CLIENT_API_KEY:-}" ]]; then
  export Drm__Security__ClientApiKey="$DRM_CLIENT_API_KEY"
fi
```

Update `deploy/management/README.md` and root `README.md` to document `X-DRM-Client-Key`.

- [x] **Step 4: Run passing install tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementInstallAssetsTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 2: Live smoke**

Run a temp server with `Drm__Security__ClientApiKey=local-client-key`, verify `/api/audit` returns 401 without `X-DRM-Client-Key`, 403 with a wrong key, and 200 with the matching key.

- [x] **Step 3: Commit**

Run:

```bash
git add README.md deploy/management src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5a-client-api-key-auth.md
git commit -m "feat: protect client APIs with API key"
```

## Self-Review

- Spec coverage: Adds opt-in authentication for non-admin client APIs while preserving admin key separation.
- Compatibility note: Existing deployments with no `Drm:Security:ClientApiKey` remain unchanged.
- Placeholder scan: No TBD/TODO placeholders.
