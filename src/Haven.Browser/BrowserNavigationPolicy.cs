/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserNavigationPolicy.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserNavigationPolicy. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using System.Net.Sockets;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

/// <summary>
/// Represents browser navigation policy and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserNavigationPolicy : IBrowserNavigationPolicy
{
    /// <summary>
    /// Performs assess async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the denied step owned by this component.
    /// </summary>
    private static BrowserNavigationAssessment Denied(Uri address, string reason) => new(address, false, reason, []);

    /// <summary>
    /// Reports whether is non public is true for the current state.
    /// </summary>
    internal static bool IsNonPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return true;
        if (address.IsIPv4MappedToIPv6) return IsNonPublic(address.MapToIPv4());
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var deprecatedSiteLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0;
            return address.IsIPv6LinkLocal
                   || address.IsIPv6Multicast
                   || deprecatedSiteLocal
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
