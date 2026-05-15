using System.Security.Cryptography;
using System.Text;

namespace Drm.Server;

internal static class ExternalShareToken
{
    private const int TokenByteLength = 32;

    public static GeneratedExternalShareToken Create()
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenByteLength));
        return new GeneratedExternalShareToken(token, Hash(token));
    }

    public static string Hash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed record GeneratedExternalShareToken(string Plaintext, string Hash);
