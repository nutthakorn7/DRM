using System.Text;
using Drm.Crypto;

namespace Drm.Container;

public sealed record ProtectedFilePackage(ProtectedFileHeader Header, AesGcmPayload Payload)
{
    public byte[] Decrypt(byte[] fileKey) =>
        EnvelopeCrypto.Decrypt(Payload, fileKey, ProtectedFileAssociatedData.For(Header));
}

internal static class ProtectedFileAssociatedData
{
    public static byte[] For(ProtectedFileHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var value = string.Join(
            '\n',
            header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            header.TenantId.ToString("D"),
            header.FileId.ToString("D"),
            header.ContentType,
            header.CreatedAtUtc.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return Encoding.UTF8.GetBytes(value);
    }
}
