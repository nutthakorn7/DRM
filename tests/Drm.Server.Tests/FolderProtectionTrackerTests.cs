using FluentAssertions;
using Drm.FolderWatcher.Service;

namespace Drm.Server.Tests;

public sealed class FolderProtectionTrackerTests
{
    [Fact]
    public void ShouldProcess_returns_true_for_new_path_and_remembers_snapshot()
    {
        var tracker = new FolderProtectionTracker();
        var now = DateTime.UtcNow;
        tracker.ShouldProcess(@"C:\a.txt", 100, now).Should().BeTrue();
        tracker.ShouldProcess(@"C:\a.txt", 100, now).Should().BeFalse();
    }

    [Fact]
    public void ShouldProcess_returns_true_when_size_or_timestamp_changes()
    {
        var tracker = new FolderProtectionTracker();
        var t1 = DateTime.UtcNow;
        tracker.ShouldProcess(@"C:\a.txt", 100, t1).Should().BeTrue();
        tracker.ShouldProcess(@"C:\a.txt", 101, t1).Should().BeTrue();
        var t2 = t1.AddSeconds(5);
        tracker.ShouldProcess(@"C:\a.txt", 101, t2).Should().BeTrue();
    }

    [Fact]
    public void RecordProtected_increments_counter_and_updates_snapshot()
    {
        var tracker = new FolderProtectionTracker();
        var now = DateTime.UtcNow;
        tracker.FilesProtected.Should().Be(0);
        tracker.RecordProtected(@"C:\b.txt", 200, now);
        tracker.FilesProtected.Should().Be(1);
        tracker.ShouldProcess(@"C:\b.txt", 200, now).Should().BeFalse();
    }
}
