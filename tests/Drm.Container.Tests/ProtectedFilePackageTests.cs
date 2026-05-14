using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Container.Tests;

public sealed class ProtectedFilePackageTests
{
    [Fact]
    public void Write_then_read_round_trips_header_and_payload()
    {
        var tenantId = TenantId.New();
        var fileId = ProtectedFileId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var pdfBytes = "%PDF-1.7 test"u8.ToArray();

        using var stream = new MemoryStream();
        ProtectedFileWriter.Write(stream, tenantId, fileId, "application/pdf", fileKey, pdfBytes);
        stream.Position = 0;

        var package = ProtectedFileReader.Read(stream);
        var decrypted = package.Decrypt(fileKey);

        package.Header.TenantId.Should().Be(tenantId.Value);
        package.Header.FileId.Should().Be(fileId.Value);
        package.Header.ContentType.Should().Be("application/pdf");
        decrypted.Should().Equal(pdfBytes);
    }
}
