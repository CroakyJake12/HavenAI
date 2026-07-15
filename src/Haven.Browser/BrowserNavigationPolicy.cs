using System.Net;
using System.Net.Sockets;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserNavigationPolicy : IBrowserNavigationPolicy
{
    public async Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || address.Scheme is not ("http" or "https"))
            return Denied(address, "Browser automation permits only absolute HTTP and HTTPS addresses.");
        if (!string.IsNullOrEmpty(address.UserInfo))
            return Denied(address, "Credentials embedded in a URL are not permitted.");
        if (string.IsNullOrWhiteSpace(address.Host))
            return Denied(address, "The address has no host.");
        if (address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return Denied(address, "Local and internal host names are blocked for model-driven browsing.");

        IPAddress[] resolved;
        try
        {
            resolved = IPAddress.TryParse(address.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(address.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return Denied(address, "The host could not be resolved safely.");
        }

        if (resolved.Length == 0) return Denied(address, "The host resolved to no addresses.");
        var blocked = resolved.FirstOrDefault(IsNonPublic);
        if (blocked is not null)
            return new BrowserNavigationAssessment(address, false, $"The host resolved to non-public address {blocked}.", resolved.Select(item => item.ToString()).ToArray());
        return new BrowserNavigationAssessment(address, true, "The address resolved only to public network addresses.", resolved.Select(item => item.ToString()).ToArray());
    }

    private static BrowserNavigationAssessment Denied(Uri address, string reason) => new(address, false, reason, []);

    internal static bool IsNonPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return true;
        if (address.IsIPv4MappedToIPv6) return IsNonPublic(address.MapToIPv4());
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                   || address.IsIPv6Multicast
                   || address.IsIPv6SiteLocal
                   || (bytes[0] & 0xFE) == 0xFC;
        }

        var value = address.GetAddressBytes();
        return value[0] switch
        {
            0 => true,
            10 => true,
            100 when value[1] is >= 64 and <= 127 => true,
            127 => true,
            169 when value[1] == 254 => true,
            172 when value[1] is >= 16 and <= 31 => true,
            192 when value[1] == 0 => true,
            192 when value[1] == 168 => true,
            198 when value[1] is 18 or 19 => true,
            >= 224 => true,
            _ => false
        };
    }
}
