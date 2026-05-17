namespace Drm.FolderWatcher.Service;

/// <summary>
/// In-memory record of files the service has already protected so the watcher
/// does not loop on its own writes (transparent protection appends a trailer
/// to the same file path).
/// </summary>
public sealed class FolderProtectionTracker
{
    private readonly object gate = new();
    private readonly Dictionary<string, (long Size, DateTime LastWriteUtc)> seen = new(StringComparer.OrdinalIgnoreCase);
    private int filesProtected;

    public int FilesProtected
    {
        get { lock (gate) return filesProtected; }
    }

    public bool ShouldProcess(string fullPath, long size, DateTime lastWriteUtc)
    {
        lock (gate)
        {
            if (seen.TryGetValue(fullPath, out var snapshot)
                && snapshot.Size == size
                && snapshot.LastWriteUtc == lastWriteUtc)
            {
                return false;
            }
            seen[fullPath] = (size, lastWriteUtc);
            return true;
        }
    }

    public void RecordProtected(string fullPath, long newSize, DateTime newLastWriteUtc)
    {
        lock (gate)
        {
            seen[fullPath] = (newSize, newLastWriteUtc);
            filesProtected++;
        }
    }
}
