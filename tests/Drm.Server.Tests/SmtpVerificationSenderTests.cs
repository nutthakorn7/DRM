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

public sealed class SmtpSenderWiringTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"drm-smtp-wiring-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(databasePath);

    [Fact]
    public void When_smtp_host_is_configured_smtp_sender_is_registered()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                b.UseSetting("Drm:Email:SmtpHost", "smtp.test.example");
            });

        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IExternalShareVerificationSender>();
        sender.Should().BeOfType<SmtpExternalShareVerificationSender>();
    }

    [Fact]
    public void When_smtp_host_is_not_configured_noop_sender_is_registered()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                // Drm:Email:SmtpHost intentionally absent
            });

        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IExternalShareVerificationSender>();
        sender.Should().BeOfType<NoopExternalShareVerificationSender>();
    }
}
