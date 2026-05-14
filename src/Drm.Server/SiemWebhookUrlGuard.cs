using System.Net;
using System.Net.Sockets;

namespace Drm.Server;

internal static class SiemWebhookUrlGuard
{
    public static async Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || IsLocalHostName(uri.Host))
        {
            return false;
        }

        await Task.CompletedTask;

        return IPAddress.TryParse(uri.Host, out var literalAddress)
            && IsPublicAddress(literalAddress);
    }

    private static bool IsLocalHostName(string host)
    {
        var normalized = host.TrimEnd('.');
        return string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return !IPAddress.IsLoopback(address)
            && !address.Equals(IPAddress.Any)
            && !address.Equals(IPAddress.IPv6Any)
            && !address.Equals(IPAddress.Broadcast)
            && !IsPrivateAddress(address)
            && !IsLinkLocalAddress(address)
            && !IsSpecialPurposeAddress(address);
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }

    private static bool IsLinkLocalAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal;
    }

    private static bool IsSpecialPurposeAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                || (bytes[0] == 192 && bytes[1] == 0)
                || (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                || bytes[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6Multicast
                || address.Equals(IPAddress.IPv6None)
                || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
                || (bytes[0] & 0xe0) != 0x20;
        }

        return true;
    }
}
