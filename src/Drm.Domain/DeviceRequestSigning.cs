using System.Security.Cryptography;
using System.Text;

namespace Drm.Domain;

public sealed record DeviceRequestSignature(DateTimeOffset SignedAtUtc, string Nonce, string SignatureBase64);

public static class DeviceRequestSigning
{
    private static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(5);

    public static string GenerateDeviceSecret()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string DeriveSigningKeyHashBase64(string deviceSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSecret);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(deviceSecret.Trim())));
    }

    public static DeviceRequestSignature Sign(string deviceSecret, string canonicalPayload)
    {
        var signedAtUtc = DateTimeOffset.UtcNow;
        var nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var signingKeyHashBase64 = DeriveSigningKeyHashBase64(deviceSecret);
        var canonical = AppendSignatureFields(canonicalPayload, signedAtUtc, nonce);
        var signatureBase64 = ComputeSignatureBase64(signingKeyHashBase64, canonical);

        return new DeviceRequestSignature(signedAtUtc, nonce, signatureBase64);
    }

    public static bool Verify(
        string signingKeyHashBase64,
        string canonicalPayload,
        DeviceRequestSignature? signature,
        DateTimeOffset nowUtc,
        TimeSpan? maxClockSkew = null)
    {
        if (string.IsNullOrWhiteSpace(signingKeyHashBase64) ||
            signature is null ||
            string.IsNullOrWhiteSpace(signature.Nonce) ||
            string.IsNullOrWhiteSpace(signature.SignatureBase64))
        {
            return false;
        }

        var clockSkew = maxClockSkew ?? DefaultClockSkew;
        if (signature.SignedAtUtc < nowUtc.Subtract(clockSkew) ||
            signature.SignedAtUtc > nowUtc.Add(clockSkew))
        {
            return false;
        }

        var canonical = AppendSignatureFields(
            canonicalPayload,
            signature.SignedAtUtc,
            signature.Nonce.Trim());
        var expected = ComputeSignatureBase64(signingKeyHashBase64, canonical);

        return FixedTimeEquals(expected, signature.SignatureBase64.Trim());
    }

    public static string RegisterDevicePayload(
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        string hostname,
        string operatingSystem,
        string agentVersion,
        bool? domainJoined,
        string? domainName,
        string? windowsUser)
        => Canonicalize(
            "register",
            tenantId,
            userId,
            deviceId,
            hostname,
            operatingSystem,
            agentVersion,
            domainJoined,
            domainName,
            windowsUser);

    public static string HeartbeatPayload(
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        string status,
        string agentVersion,
        bool? domainJoined,
        string? domainName,
        string? windowsUser)
        => Canonicalize(
            "heartbeat",
            tenantId,
            userId,
            deviceId,
            status,
            agentVersion,
            domainJoined,
            domainName,
            windowsUser);

    public static string UnwrapPayload(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission)
        => Canonicalize(
            "unwrap",
            tenantId,
            fileId,
            userId,
            deviceId,
            requestedPermission);

    private static string AppendSignatureFields(
        string canonicalPayload,
        DateTimeOffset signedAtUtc,
        string nonce)
        => string.Join('\n', canonicalPayload, signedAtUtc.ToUnixTimeSeconds().ToString(), nonce.Trim());

    private static string ComputeSignatureBase64(string signingKeyHashBase64, string canonical)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(signingKeyHashBase64.Trim());
        }
        catch (FormatException)
        {
            return string.Empty;
        }

        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Canonicalize(params object?[] values)
        => string.Join('\n', values.Select(Normalize));

    private static string Normalize(object? value)
        => value switch
        {
            null => string.Empty,
            Guid guid => guid.ToString("D"),
            bool boolean => boolean ? "true" : "false",
            string text => text.Trim(),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
        };

    private static bool FixedTimeEquals(string expectedBase64, string actualBase64)
    {
        try
        {
            var expected = Convert.FromBase64String(expectedBase64);
            var actual = Convert.FromBase64String(actualBase64);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
