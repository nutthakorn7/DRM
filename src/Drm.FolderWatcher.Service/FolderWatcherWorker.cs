using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Drm.FolderWatcher.Service;

public sealed class FolderWatcherWorker : BackgroundService
{
    private readonly IOptionsMonitor<FolderWatcherOptions> options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly FolderProtector protector;
    private readonly FolderProtectionTracker tracker;
    private readonly ILogger<FolderWatcherWorker> logger;
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);

    public FolderWatcherWorker(
        IOptionsMonitor<FolderWatcherOptions> options,
        IHttpClientFactory httpClientFactory,
        FolderProtector protector,
        FolderProtectionTracker tracker,
        ILogger<FolderWatcherWorker> logger)
    {
        this.options = options;
        this.httpClientFactory = httpClientFactory;
        this.protector = protector;
        this.tracker = tracker;
        this.logger = logger;
    }

    private CancellationToken stoppingToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.stoppingToken = stoppingToken;
        while (!stoppingToken.IsCancellationRequested)
        {
            var opt = options.CurrentValue;
            try
            {
                var watchedFolders = await FetchWatchedFoldersAsync(opt, stoppingToken);
                SyncWatchers(watchedFolders);
                await ReportStatusAsync(opt, watchedFolders.Count > 0 ? "ok" : "idle", stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Folder watcher poll failed");
                await ReportStatusAsync(opt, "error", stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, opt.PollIntervalSeconds)), stoppingToken);
        }

        foreach (var w in watchers.Values) w.Dispose();
        watchers.Clear();
    }

    private async Task<List<string>> FetchWatchedFoldersAsync(FolderWatcherOptions opt, CancellationToken cancellationToken)
    {
        var client = CreateClient(opt);
        using var response = await client.GetAsync(
            $"/api/admin/folder-watcher/config?tenantId={opt.TenantId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<string>();
        }
        response.EnsureSuccessStatusCode();
        var config = await response.Content.ReadFromJsonAsync<RemoteConfig>(cancellationToken: cancellationToken);
        if (config is null || !config.Enabled || config.WatchedFolders is null)
        {
            return new List<string>();
        }
        return config.WatchedFolders.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    private void SyncWatchers(IReadOnlyList<string> wantedFolders)
    {
        var wantedSet = wantedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in watchers.Keys.ToList())
        {
            if (!wantedSet.Contains(existing))
            {
                watchers[existing].Dispose();
                watchers.Remove(existing);
                logger.LogInformation("Stopped watching {Folder}", existing);
            }
        }

        foreach (var folder in wantedFolders)
        {
            if (watchers.ContainsKey(folder)) continue;
            if (!Directory.Exists(folder))
            {
                logger.LogWarning("Configured folder does not exist: {Folder}", folder);
                continue;
            }
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            watcher.Created += (_, e) => _ = HandleEventAsync(folder, e.FullPath);
            watcher.Changed += (_, e) => _ = HandleEventAsync(folder, e.FullPath);
            watcher.Renamed += (_, e) => _ = HandleEventAsync(folder, e.FullPath);
            watchers[folder] = watcher;
            logger.LogInformation("Started watching {Folder}", folder);
        }
    }

    private async Task HandleEventAsync(string folder, string fullPath)
    {
        if (Directory.Exists(fullPath)) return;
        if (stoppingToken.IsCancellationRequested) return;
        try
        {
            await protector.ProtectAsync(folder, fullPath, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Service shutting down — drop in-flight protect quietly.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Watcher handler failed for {Path}", fullPath);
        }
    }

    private async Task ReportStatusAsync(FolderWatcherOptions opt, string status, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(opt);
            await client.PostAsJsonAsync(
                "/api/admin/folder-watcher/report",
                new
                {
                    tenantId = opt.TenantId,
                    hostname = Environment.MachineName,
                    status,
                    filesProtected = tracker.FilesProtected
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to report watcher status");
        }
    }

    private HttpClient CreateClient(FolderWatcherOptions opt)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(opt.ServerUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DRM-Admin-Key", opt.AdminApiKey);
        return client;
    }

    private sealed record RemoteConfig(
        Guid TenantId,
        IReadOnlyList<RemoteWatchedFolder> WatchedFolders,
        bool Enabled);

    private sealed record RemoteWatchedFolder(string Path, Guid? PolicyTemplateId);
}
