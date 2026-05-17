using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Drm.UI.Tests;

/// <summary>
/// Spawns the Drm.Server process on a free localhost port for the lifetime
/// of the test class so Playwright can drive a real browser against a real
/// HTTP endpoint. Disposed at the end of the test class.
///
/// We can't use WebApplicationFactory + TestServer here: TestServer's
/// HttpClient is in-memory and doesn't bind a socket, but Playwright needs
/// a real URL it can navigate to.
/// </summary>
public sealed class DrmServerFixture : IAsyncLifetime
{
    private Process? process;
    private string databasePath = string.Empty;

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var port = GetFreeTcpPort();
        BaseUrl = $"http://localhost:{port}";
        databasePath = Path.Combine(Path.GetTempPath(), $"drm-ui-tests-{Guid.NewGuid():N}.db");

        var repoRoot = FindRepoRoot();
        var serverProject = Path.Combine(repoRoot, "src", "Drm.Server", "Drm.Server.csproj");

        process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --no-launch-profile --project \"{serverProject}\" --urls \"{BaseUrl}\"",
                EnvironmentVariables =
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["ConnectionStrings__DrmDb"] = $"Data Source={databasePath}",
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = repoRoot,
            },
            EnableRaisingEvents = true,
        };

        process.Start();

        // Wait up to 45 seconds for /healthz to come back 200.
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var ready = false;
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await httpClient.GetAsync($"{BaseUrl}/healthz");
                if (response.IsSuccessStatusCode)
                {
                    ready = true;
                    break;
                }
            }
            catch
            {
                // Server not up yet; back off.
            }

            await Task.Delay(500);
        }

        if (!ready)
        {
            throw new InvalidOperationException(
                $"Drm.Server did not start on {BaseUrl} within 45 seconds.");
        }
    }

    public async Task DisposeAsync()
    {
        if (process is not null && !process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            await process.WaitForExitAsync();
            process.Dispose();
        }

        if (!string.IsNullOrEmpty(databasePath) && File.Exists(databasePath))
        {
            try { File.Delete(databasePath); } catch { /* best-effort */ }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Drm.Server")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Drm.Server project from test assembly.");
    }
}
