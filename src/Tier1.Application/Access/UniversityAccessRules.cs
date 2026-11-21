using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Tier1.Domain.Common;

namespace Tier1.Application.Access;

/// <summary>
/// ADR-0016: which email domains an approved root covers. A root covers itself and every subdomain under it, matched on label
/// boundaries: capu.edu.eg covers eng.capu.edu.eg, never xcapu.edu.eg nor capu.edu.eg.evil.com.
/// </summary>
public static partial class EmailDomainRules
{
    // One DNS label: letters, digits and inner hyphens, 1 to 63 characters.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Label();

    [GeneratedRegex("^[a-z]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopLabel();

    /// <summary>A valid host name: two labels or more, each a DNS label, ending in a letters-only label, 253 characters at most.</summary>
    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253) return false;
        var labels = host.Split('.');
        return labels.Length >= 2 && labels.All(l => Label().IsMatch(l)) && TopLabel().IsMatch(labels[^1]);
    }

    /// <summary>A root as the administrator types it ("@Capu.edu.eg ", "capu.edu.eg."), checked and in its stored form.</summary>
    public static string NormalizeRoot(string? value)
    {
        var root = (value ?? "").Trim().ToLowerInvariant().TrimStart('@').TrimEnd('.');
        if (!IsValidHost(root))
            throw new DomainException("invalid_domain", "اكتب نطاقًا جذريًا صحيحًا مثل capu.edu.eg (حروف إنجليزية وأرقام وشرطات، ونقطة بين الأجزاء).",
                "Enter a valid root domain such as capu.edu.eg (letters, digits and hyphens, with dots between the parts).", 400) { Field = "domain" };
        return root;
    }

    /// <summary>The domain part of a normalized email, or null.</summary>
    public static string? DomainOf(string email) => email.LastIndexOf('@') is var at and > 0 ? email[(at + 1)..] : null;

    /// <summary>The root covers the domain: the same, or a subdomain of it on a label boundary.</summary>
    public static bool Covers(string root, string domain) =>
        domain.Equals(root, StringComparison.Ordinal) || domain.EndsWith("." + root, StringComparison.Ordinal);

    /// <summary>The first root that covers the email's domain, or null (also null for a malformed domain).</summary>
    public static string? MatchingRoot(string email, IEnumerable<string> roots) =>
        DomainOf(email) is { } domain && IsValidHost(domain) ? roots.Where(r => Covers(r, domain)).OrderByDescending(r => r.Length).FirstOrDefault() : null;
}

/// <summary>
/// ADR-0016: allowed addresses and ranges, IPv4 and IPv6. An entry is stored as its network in CIDR form; a single address is a
/// /32 or /128 network. IPv4 addresses seen through an IPv6 socket (::ffff:a.b.c.d) are compared as IPv4.
/// </summary>
public static partial class NetworkRules
{
    [GeneratedRegex(@"^\d{1,3}(?:\.\d{1,3}){3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedQuad();

    /// <summary>An address as the server sees it, with IPv4-mapped IPv6 turned back into IPv4; null when it is not an address.</summary>
    public static IPAddress? ParseAddress(string? value)
    {
        if (!IPAddress.TryParse((value ?? "").Trim(), out var ip)) return null;
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    /// <summary>An entry as the administrator types it ("41.65.100.20", "192.168.1.0/24", "2001:db8::/32"), checked and normalized.</summary>
    public static string NormalizeEntry(string? value)
    {
        var text = (value ?? "").Trim();
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        var host = slash < 0 ? text : text[..slash];
        // IPAddress.TryParse also reads "10" or "10.1" (as 0.0.0.10 and 10.0.0.1) and zone ids; an entry is written in full.
        var written = host.Contains(':', StringComparison.Ordinal) ? !host.Contains('%', StringComparison.Ordinal) : DottedQuad().IsMatch(host);
        var ip = written ? ParseAddress(host) : null;
        if (ip is null || ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw Invalid("اكتب عنوان IP صحيحًا (مثل 41.65.100.20 أو 2001:db8::1) أو نطاقًا بصيغة CIDR (مثل 192.168.1.0/24).",
                "Enter a valid IP address (such as 41.65.100.20 or 2001:db8::1) or a CIDR range (such as 192.168.1.0/24).");
        var bits = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = bits;
        if (slash >= 0 && !(int.TryParse(text[(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out prefix) && prefix <= bits))
            throw Invalid($"طول البادئة بعد / من 1 إلى {bits}.", $"The prefix length after / is 1 to {bits}.");
        if (prefix == 0)
            throw Invalid("النطاق /0 يسمح بكل العناوين؛ أوقف القيد بدل ذلك.", "A /0 range allows every address; turn the restriction off instead.");
        var network = NetworkOf(ip, prefix);
        if (!network.Equals(ip))
            throw Invalid($"العنوان {ip} ليس بداية النطاق /{prefix}. هل تقصد {network}/{prefix}؟",
                $"{ip} is not the start of a /{prefix} range. Did you mean {network}/{prefix}?");
        return $"{network}/{prefix}";
    }

    /// <summary>The address is inside the stored entry.</summary>
    public static bool Contains(string cidr, IPAddress address) =>
        IPNetwork.TryParse(cidr, out var network) && network.Contains(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static IPAddress NetworkOf(IPAddress ip, int prefix)
    {
        var bytes = ip.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - keep));
        }
        return new IPAddress(bytes);
    }

    private static DomainException Invalid(string ar, string en) => new("invalid_ip", ar, en, 400) { Field = "value" };
}

/// <summary>How a request's address stands against the allowlists.</summary>
public enum IpDecision
{
    /// <summary>No list applies to this user: the normal unrestricted behaviour.</summary>
    Unrestricted,

    /// <summary>The address is in the enforced global list.</summary>
    AllowedGlobal,

    /// <summary>The address is in the user's own enforced list.</summary>
    AllowedUser,

    /// <summary>A list applies and the address is in none of the applying lists.</summary>
    Denied,
}

/// <summary>
/// ADR-0016: the allowlists' logic, apart from where the rules are stored. The global list applies when it is enforced and has
/// entries; the user's list applies when the user is restricted and has entries. With neither, access is unrestricted. Otherwise
/// the address must be in one of the lists that apply: a user's own entries add to the global ones, never replace them.
/// </summary>
public static class IpAllowlist
{
    public static IpDecision Decide(IPAddress? address, bool globalOn, IReadOnlyList<string> global, bool userOn, IReadOnlyList<string> own)
    {
        var globalApplies = globalOn && global.Count > 0;
        var userApplies = userOn && own.Count > 0;
        if (!globalApplies && !userApplies) return IpDecision.Unrestricted;
        if (address is null) return IpDecision.Denied;
        if (globalApplies && global.Any(c => NetworkRules.Contains(c, address))) return IpDecision.AllowedGlobal;
        if (userApplies && own.Any(c => NetworkRules.Contains(c, address))) return IpDecision.AllowedUser;
        return IpDecision.Denied;
    }

    public static string Name(IpDecision d) => d switch
    {
        IpDecision.AllowedGlobal => "allowed_global",
        IpDecision.AllowedUser => "allowed_user",
        IpDecision.Denied => "denied",
        _ => "unrestricted",
    };
}
