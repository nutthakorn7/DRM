using Drm.Agent.Core;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class ViewerPermissionStateTests
{
    [Fact]
    public void None_denies_print_copy_and_export()
    {
        var state = ViewerPermissionState.From(Permission.None);

        state.CanPrint.Should().BeFalse();
        state.CanCopy.Should().BeFalse();
        state.CanExportOriginal.Should().BeFalse();
        state.Allows(ViewerControlledAction.Print).Should().BeFalse();
        state.Allows(ViewerControlledAction.Copy).Should().BeFalse();
        state.Allows(ViewerControlledAction.ExportOriginal).Should().BeFalse();
    }

    [Fact]
    public void Print_and_copy_permissions_allow_only_those_actions()
    {
        var state = ViewerPermissionState.From(Permission.View | Permission.Print | Permission.Copy);

        state.CanPrint.Should().BeTrue();
        state.CanCopy.Should().BeTrue();
        state.CanExportOriginal.Should().BeFalse();
        state.Allows(ViewerControlledAction.Print).Should().BeTrue();
        state.Allows(ViewerControlledAction.Copy).Should().BeTrue();
        state.Allows(ViewerControlledAction.ExportOriginal).Should().BeFalse();
    }

    [Fact]
    public void Export_original_permission_allows_only_export()
    {
        var state = ViewerPermissionState.From(Permission.View | Permission.ExportOriginal);

        state.CanPrint.Should().BeFalse();
        state.CanCopy.Should().BeFalse();
        state.CanExportOriginal.Should().BeTrue();
        state.Allows(ViewerControlledAction.Print).Should().BeFalse();
        state.Allows(ViewerControlledAction.Copy).Should().BeFalse();
        state.Allows(ViewerControlledAction.ExportOriginal).Should().BeTrue();
    }
}
