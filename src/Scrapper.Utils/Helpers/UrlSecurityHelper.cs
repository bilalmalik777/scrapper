using System.Net;
using System.Net.Sockets;

namespace Scrapper.Utils.Helpers;

public static class UrlSecurityHelper
{
    private static readonly string[] AllowedSchemes = ["http", "https"];

    public static bool TryParseAllowedUrl(string url, out Uri? uri, out string? reason)
    {
        uri = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "URL is required.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            reason = "The URL is malformed.";
            return false;
        }

        if (!AllowedSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            reason = "Only http and https URLs are supported.";
            return false;
        }

        if (IsBlockedHost(parsed.Host))
        {
            reason = "Access to internal or private network addresses is not allowed.";
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool IsBlockedHost(string host)
    {
        host = host.Trim().ToLowerInvariant();

        if (host is "localhost" or "0.0.0.0" or "[::1]" or "::1")
        {
            return true;
        }

        if (host.EndsWith(".local") || host.EndsWith(".internal"))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IsPrivateOrReservedIp(ip);
        }

        return false;
    }

    public static async Task<bool> ResolvesToBlockedAddressAsync(string host, CancellationToken cancellationToken = default)
    {
        if (IPAddress.TryParse(host, out var directIp))
        {
            return IsPrivateOrReservedIp(directIp);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length == 0 || addresses.Any(IsPrivateOrReservedIp);
        }
        catch (SocketException)
        {
            return true;
        }
    }

    public static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                10 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                0 => true,
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }

            // fc00::/7 unique local addresses
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }
        }

        return false;
    }
}
