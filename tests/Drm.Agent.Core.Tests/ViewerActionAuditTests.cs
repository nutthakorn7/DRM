using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class ViewerActionAuditTests
{
    [Fact]
    public void Allowed_print_creates_print_allowed_audit_record()
    {
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var fileId = Guid.NewGuid();
        var atUtc = DateTimeOffset.Parse("2026-05-15T02:00:00Z");

        var record = ViewerActionAudit.Create(
            identity,
            fileId,
            ViewerControlledAction.Print,
            allowed: true,
            atUtc);

        record.Should().BeEquivalentTo(new
        {
            identity.TenantId,
            identity.UserId,
            identity.DeviceId,
            FileId = (Guid?)fileId,
            EventType = "print_allowed",
            ReasonCode = "allowed",
            CreatedAtUtc = atUtc
        });
    }

    [Fact]
    public void Blocked_copy_creates_missing_copy_permission_audit_record()
    {
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var fileId = Guid.NewGuid();
        var atUtc = DateTimeOffset.Parse("2026-05-15T02:00:00Z");

        var record = ViewerActionAudit.Create(
            identity,
            fileId,
            ViewerControlledAction.Copy,
            allowed: false,
            atUtc);

        record.EventType.Should().Be("copy_blocked");
        record.ReasonCode.Should().Be("missing_copy_permission");
        record.FileId.Should().Be(fileId);
    }

    [Fact]
    public void Blocked_export_creates_missing_export_permission_audit_record()
    {
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var fileId = Guid.NewGuid();
        var atUtc = DateTimeOffset.Parse("2026-05-15T02:00:00Z");

        var record = ViewerActionAudit.Create(
            identity,
            fileId,
            ViewerControlledAction.ExportOriginal,
            allowed: false,
            atUtc);

        record.EventType.Should().Be("export_blocked");
        record.ReasonCode.Should().Be("missing_export_permission");
        record.CreatedAtUtc.Should().Be(atUtc);
    }
}
