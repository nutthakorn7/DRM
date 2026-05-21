using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class MailtoEmailComposerTests
{
    [Fact]
    public void Compose_passes_mailto_url_with_url_encoded_subject_and_body()
    {
        var spy = new RecordingMailtoProtocolHandler();
        var composer = new MailtoEmailComposer(spy);

        var result = composer.Compose(new EmailComposition(
            Recipient: "malee@xyz.com",
            Subject: "Encrypted file: Q4 Sales Report.pdf.drmx",
            Body: "Hi,\n\nShare URL: https://drm.zcr.ai/share/?token=abc&email=malee%40xyz.com",
            AttachmentPaths: []));

        result.ComposerOpened.Should().BeTrue();
        result.AttachmentInlined.Should().BeFalse(
            "mailto: cannot carry attachments per RFC 2368");
        result.FailureReason.Should().BeNull();
        spy.LastUrl.Should().NotBeNull();
        spy.LastUrl!.Should().StartWith("mailto:malee%40xyz.com");
        spy.LastUrl.Should().Contain("subject=Encrypted%20file");
        spy.LastUrl.Should().Contain(".drmx");
        // ampersands inside the body must be percent-encoded so they don't
        // bleed into mailto's own query parameters.
        spy.LastUrl.Should().Contain("%26").And.NotContain("token=abc&email=");
    }

    [Fact]
    public void Compose_returns_failure_reason_when_protocol_handler_throws()
    {
        var composer = new MailtoEmailComposer(new ThrowingMailtoProtocolHandler());

        var result = composer.Compose(new EmailComposition(
            Recipient: "x@y.com",
            Subject: "s",
            Body: "b",
            AttachmentPaths: []));

        result.ComposerOpened.Should().BeFalse();
        result.AttachmentInlined.Should().BeFalse();
        result.FailureReason.Should().Be("no default mail client");
    }

    private sealed class RecordingMailtoProtocolHandler : IMailtoProtocolHandler
    {
        public string? LastUrl { get; private set; }
        public void Open(string mailtoUrl) => LastUrl = mailtoUrl;
    }

    private sealed class ThrowingMailtoProtocolHandler : IMailtoProtocolHandler
    {
        public void Open(string mailtoUrl)
            => throw new InvalidOperationException("no default mail client");
    }
}
