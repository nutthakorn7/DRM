namespace Drm.FolderWatcher.Service;

public sealed class FolderWatcherOptions
{
    public string ServerUrl { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string AdminApiKey { get; set; } = string.Empty;
    public string TrailerSecret { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 60;
}
