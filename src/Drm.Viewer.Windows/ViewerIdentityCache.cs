using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Drm.Agent.Core;

namespace Drm.Viewer.Windows;

internal static class ViewerIdentityCache
{
    private const int SaltLengthBytes = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static AgentIdentityCacheEntry? TryReadDefault()
    {
        var filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "zcrDRM",
            "identity.bin");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var blob = File.ReadAllBytes(filePath);
            if (blob.Length <= SaltLengthBytes)
            {
                return null;
            }

            var salt = new byte[SaltLengthBytes];
            Buffer.BlockCopy(blob, 0, salt, 0, SaltLengthBytes);
            var cipher = new byte[blob.Length - SaltLengthBytes];
            Buffer.BlockCopy(blob, SaltLengthBytes, cipher, 0, cipher.Length);

            var plaintext = ProtectedData.Unprotect(cipher, salt, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AgentIdentityCacheEntry>(plaintext, JsonOptions);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
