using Drm.Cli;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Cli.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void CliParser_parses_protect_command_with_policy_template_and_recipients()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var recipientGroupId = Guid.NewGuid();

        var result = CliParser.Parse([
            "protect",
            "--server-url", "https://drm.example",
            "--tenant-id", tenantId.ToString(),
            "--user-id", userId.ToString(),
            "--file", "/work/contract.docx",
            "--permissions", "View, Print, ExportOriginal",
            "--policy-template-id", templateId.ToString(),
            "--recipient-user-id", recipientUserId.ToString(),
            "--recipient-group-id", recipientGroupId.ToString(),
            "--client-api-key", "client-secret",
            "--delete-original"
        ]);

        result.IsSuccess.Should().BeTrue(result.Error);
        var command = result.Command.Should().BeOfType<ProtectCommandOptions>().Subject;
        command.ServerUrl.Should().Be("https://drm.example");
        command.TenantId.Should().Be(tenantId);
        command.UserId.Should().Be(userId);
        command.FilePath.Should().Be("/work/contract.docx");
        command.Permissions.Should().Be(Permission.View | Permission.Print | Permission.ExportOriginal);
        command.PolicyTemplateId.Should().Be(templateId);
        command.ClientApiKey.Should().Be("client-secret");
        command.DeleteOriginal.Should().BeTrue();
        command.Recipients.Should().Equal([
            new CliRecipient("User", recipientUserId),
            new CliRecipient("Group", recipientGroupId)
        ]);
    }

    [Fact]
    public void CliParser_parses_open_command()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var result = CliParser.Parse([
            "open",
            "--server-url", "https://drm.example",
            "--user-id", userId.ToString(),
            "--device-id", deviceId.ToString(),
            "--file", "/work/contract.docx.drmx",
            "--output", "/work/contract.docx"
        ]);

        result.IsSuccess.Should().BeTrue(result.Error);
        var command = result.Command.Should().BeOfType<OpenCommandOptions>().Subject;
        command.ServerUrl.Should().Be("https://drm.example");
        command.UserId.Should().Be(userId);
        command.DeviceId.Should().Be(deviceId);
        command.FilePath.Should().Be("/work/contract.docx.drmx");
        command.OutputPath.Should().Be("/work/contract.docx");
    }

    [Theory]
    [InlineData("unknown command")]
    [InlineData("missing required option")]
    public void CliParser_rejects_invalid_commands(string scenario)
    {
        var args = scenario == "unknown command"
            ? new[] { "sync" }
            : ["protect", "--server-url", "https://drm.example"];

        var result = CliParser.Parse(args);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
