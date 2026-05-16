using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Drm.Server.Tests;

public sealed class SmtpVerificationSenderBuildMessageTests
{
    private static readonly SmtpEmailSettings TestSettings = new()
    {
        SmtpHost = "smtp.test.example",
        FromAddress = "noreply@test.example",
        FromName = "Test DRM"
    };

    private static readonly ExternalShareVerificationMessage TestMessage = new(
        TenantId: Guid.NewGuid(),
        ShareLinkId: Guid.NewGuid(),
        VerificationId: Guid.NewGuid(),
        GuestEmail: "guest@example.com",
        Code: "123456",
        ExpiresAtUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BuildMessage_sets_to_address_to_guest_email()
    {
        var mime = SmtpExternalShareVerificationSender.BuildMessage(TestMessage, TestSettings);
        mime.To.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("guest@example.com");
    }

    [Fact]
    public void BuildMessage_sets_from_address_from_settings()
    {
        var mime = SmtpExternalShareVerificationSender.BuildMessage(TestMessage, TestSettings);
        mime.From.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("noreply@test.example");
    }

    [Fact]
    public void BuildMessage_sets_from_name_from_settings()
    {
        var mime = SmtpExternalShareVerificationSender.BuildMessage(TestMessage, TestSettings);
        mime.From.Mailboxes.Should().ContainSingle()
            .Which.Name.Should().Be("Test DRM");
    }

    [Fact]
    public void BuildMessage_body_contains_verification_code()
    {
        var mime = SmtpExternalShareVerificationSender.BuildMessage(TestMessage, TestSettings);
        var body = ((TextPart?)mime.Body)?.Text;
        body.Should().Contain("123456");
    }

    [Fact]
    public void BuildMessage_body_contains_expiry_year()
    {
        var mime = SmtpExternalShareVerificationSender.BuildMessage(TestMessage, TestSettings);
        var body = ((TextPart?)mime.Body)?.Text;
        body.Should().Contain("2026");
    }

    [Fact]
    public void BuildMessage_subject_is_non_empty()
    {
        var mime = SmtpExternalShareVerificationSender.BuildMessage(TestMessage, TestSettings);
        mime.Subject.Should().NotBeNullOrWhiteSpace();
    }
}
