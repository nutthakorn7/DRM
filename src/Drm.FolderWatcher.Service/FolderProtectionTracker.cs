namespace Drm.FolderWatcher.Service;

/// <summary>
/// In-memory record of files the service has already protected so the watcher
/// does not loop on its own writes (transparent protection appends a trailer
/// to the same file path).
/// </summary>
public sealed class FolderProtectionTracker
{
    /// <summary>
    /// Cap on remembered paths. Once exceeded, oldest entries are evicted in
    /// insertion order to keep memory bounded on long-running services.
    /// </summary>
    public const int MaxTrackedEntries = 50_000;

    private readonly object gate = new();
    private readonly LinkedList<string> insertionOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, long Size, DateTime LastWriteUtc)> seen =
        new(StringComparer.OrdinalIgnoreCase);
    private int filesProtected;

    public int FilesProtected
    {
        get { lock (gate) return filesProtected; }
    }

    public int TrackedEntries
    {
        get { lock (gate) return seen.Count; }
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
            Upsert(fullPath, size, lastWriteUtc);
            return true;
        }
    }

    public void RecordProtected(string fullPath, long newSize, DateTime newLastWriteUtc)
    {
        lock (gate)
        {
            Upsert(fullPath, newSize, newLastWriteUtc);
            filesProtected++;
        }
    }

    private void Upsert(string fullPath, long size, DateTime lastWriteUtc)
    {
        if (seen.TryGetValue(fullPath, out var existing))
        {
            insertionOrder.Remove(existing.Node);
        }
        var node = insertionOrder.AddLast(fullPath);
        seen[fullPath] = (node, size, lastWriteUtc);

        while (seen.Count > MaxTrackedEntries && insertionOrder.First is { } oldest)
        {
            seen.Remove(oldest.Value);
            insertionOrder.RemoveFirst();
        }
    }
}
