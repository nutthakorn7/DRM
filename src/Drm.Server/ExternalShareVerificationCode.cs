using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Drm.Server;

internal static class ExternalShareVerificationCode
{
    public static string Generate()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static string Hash(Guid verificationId, string code)
    {
        var normalizedCode = (code ?? string.Empty).Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{verificationId:N}:{normalizedCode}"));
        return Convert.ToBase64String(hash);
    }

    public static bool Matches(Guid verificationId, string code, string expectedHash)
    {
        var submittedBytes = Encoding.UTF8.GetBytes(Hash(verificationId, code));
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);
        return submittedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(submittedBytes, expectedBytes);
    }
}
