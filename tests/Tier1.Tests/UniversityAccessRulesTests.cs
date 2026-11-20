using System.Net;
using Tier1.Application.Access;
using Tier1.Domain.Common;

namespace Tier1.Tests;

/// <summary>ADR-0016: approved university email domains and the IP allowlists, as pure rules.</summary>
public sealed class UniversityAccessRulesTests
{
    private static readonly string[] Roots = ["capu.edu.eg", "helwan.edu.eg"];

    [Theory]
    [InlineData("username@capu.edu.eg", "capu.edu.eg")]
    [InlineData("username@eng.capu.edu.eg", "capu.edu.eg")]
    [InlineData("username@med.capu.edu.eg", "capu.edu.eg")]
    [InlineData("username@commerce.capu.edu.eg", "capu.edu.eg")]
    [InlineData("username@deep.sub.capu.edu.eg", "capu.edu.eg")]
    [InlineData("username@helwan.edu.eg", "helwan.edu.eg")]
    [InlineData("username@eng.helwan.edu.eg", "helwan.edu.eg")]
    [InlineData("username@any-subdomain.helwan.edu.eg", "helwan.edu.eg")]
    public void A_root_and_every_subdomain_under_it_are_accepted(string email, string root) =>
        Assert.Equal(root, EmailDomainRules.MatchingRoot(email, Roots));

    [Theory]
    [InlineData("username@gmail.com")]
    [InlineData("username@xcapu.edu.eg")] // not on a label boundary
    [InlineData("username@capu.edu.eg.evil.com")] // the root inside another domain
    [InlineData("username@capuedu.eg")]
    [InlineData("username@edu.eg")] // the parent of a root
    [InlineData("username@-eng.capu.edu.eg")] // not a DNS label
    [InlineData("username@eng..capu.edu.eg")]
    [InlineData("no-domain")]
    public void Any_other_domain_is_refused(string email) => Assert.Null(EmailDomainRules.MatchingRoot(email, Roots));

    [Fact]
    public void A_nested_root_is_the_more_specific_match() =>
        Assert.Equal("eng.capu.edu.eg", EmailDomainRules.MatchingRoot("a@x.eng.capu.edu.eg", ["capu.edu.eg", "eng.capu.edu.eg"]));

    [Theory]
    [InlineData("capu.edu.eg", "capu.edu.eg")]
    [InlineData("  @Alex.EDU.eg. ", "alex.edu.eg")]
    [InlineData("new-uni.edu.eg", "new-uni.edu.eg")]
    public void A_root_is_stored_lower_case_without_at_sign_or_trailing_dot(string typed, string stored) =>
        Assert.Equal(stored, EmailDomainRules.NormalizeRoot(typed));

    [Theory]
    [InlineData("")]
    [InlineData("localhost")] // one label
    [InlineData("capu.edu.eg/x")]
    [InlineData("capu edu.eg")]
    [InlineData("-capu.edu.eg")]
    [InlineData("capu.edu.123")] // the top label is letters only
    [InlineData("*.capu.edu.eg")] // a root covers its subdomains already; no wildcards
    public void An_invalid_root_is_refused_with_its_field(string typed)
    {
        var e = Assert.Throws<DomainException>(() => EmailDomainRules.NormalizeRoot(typed));
        Assert.Equal(("invalid_domain", 400, "domain"), (e.Code, e.Status, e.Field));
    }

    [Theory]
    [InlineData("192.168.1.10", "192.168.1.10/32")]
    [InlineData("41.65.100.20", "41.65.100.20/32")]
    [InlineData("192.168.1.0/24", "192.168.1.0/24")]
    [InlineData("41.65.100.0/24", "41.65.100.0/24")]
    [InlineData(" 10.0.0.0/8 ", "10.0.0.0/8")]
    [InlineData("2001:db8::1", "2001:db8::1/128")]
    [InlineData("2001:DB8::/32", "2001:db8::/32")]
    [InlineData("::ffff:192.168.1.10", "192.168.1.10/32")] // an IPv4 address written as IPv6 is IPv4
    public void Addresses_and_ranges_are_stored_as_networks(string typed, string stored) =>
        Assert.Equal(stored, NetworkRules.NormalizeEntry(typed));

    [Theory]
    [InlineData("")]
    [InlineData("300.1.1.1")]
    [InlineData("10")] // IPAddress.TryParse would read 0.0.0.10
    [InlineData("10.1")]
    [InlineData("192.168.1.0/33")]
    [InlineData("192.168.1.0/x")]
    [InlineData("0.0.0.0/0")] // everything: turn the restriction off instead
    [InlineData("192.168.1.10/24")] // not the start of the range
    [InlineData("fe80::1%eth0")]
    [InlineData("example.com")]
    public void An_invalid_address_or_range_is_refused_with_its_field(string typed)
    {
        var e = Assert.Throws<DomainException>(() => NetworkRules.NormalizeEntry(typed));
        Assert.Equal(("invalid_ip", 400, "value"), (e.Code, e.Status, e.Field));
    }

    [Fact]
    public void A_range_not_at_its_start_suggests_the_right_one() =>
        Assert.Contains("192.168.1.0/24", Assert.Throws<DomainException>(() => NetworkRules.NormalizeEntry("192.168.1.10/24")).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.77", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("41.65.100.20/32", "41.65.100.20", true)]
    [InlineData("41.65.100.20/32", "41.65.100.21", false)]
    [InlineData("192.168.1.0/24", "::ffff:192.168.1.5", true)] // seen through a dual-stack socket
    [InlineData("2001:db8::/32", "2001:db8:1::5", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    [InlineData("2001:db8::/32", "192.168.1.5", false)]
    public void An_address_is_inside_its_network(string cidr, string ip, bool inside) =>
        Assert.Equal(inside, NetworkRules.Contains(cidr, IPAddress.Parse(ip)));

    private static readonly string[] Campus = ["41.65.100.0/24"];
    private static readonly string[] Home = ["197.50.1.7/32"];

    [Fact]
    public void With_no_list_applying_access_is_unrestricted()
    {
        var ip = IPAddress.Parse("8.8.8.8");
        Assert.Equal(IpDecision.Unrestricted, IpAllowlist.Decide(ip, false, Campus, false, Home));
        // A list switched on but empty does not shut everyone out.
        Assert.Equal(IpDecision.Unrestricted, IpAllowlist.Decide(ip, true, [], true, []));
    }

    [Fact]
    public void The_global_list_alone_allows_its_addresses_to_everyone()
    {
        Assert.Equal(IpDecision.AllowedGlobal, IpAllowlist.Decide(IPAddress.Parse("41.65.100.9"), true, Campus, false, []));
        Assert.Equal(IpDecision.Denied, IpAllowlist.Decide(IPAddress.Parse("197.50.1.7"), true, Campus, false, Home));
    }

    [Fact]
    public void A_users_own_list_adds_to_the_global_one()
    {
        // Global on, user on: either list lets the user in, with no need to repeat the campus in the user's list.
        Assert.Equal(IpDecision.AllowedGlobal, IpAllowlist.Decide(IPAddress.Parse("41.65.100.9"), true, Campus, true, Home));
        Assert.Equal(IpDecision.AllowedUser, IpAllowlist.Decide(IPAddress.Parse("197.50.1.7"), true, Campus, true, Home));
        Assert.Equal(IpDecision.Denied, IpAllowlist.Decide(IPAddress.Parse("8.8.8.8"), true, Campus, true, Home));
    }

    [Fact]
    public void A_users_own_list_alone_restricts_that_user()
    {
        Assert.Equal(IpDecision.AllowedUser, IpAllowlist.Decide(IPAddress.Parse("197.50.1.7"), false, Campus, true, Home));
        // The global entries do not count while the global list is off.
        Assert.Equal(IpDecision.Denied, IpAllowlist.Decide(IPAddress.Parse("41.65.100.9"), false, Campus, true, Home));
    }

    [Fact]
    public void An_unknown_address_is_denied_where_a_list_applies() =>
        Assert.Equal(IpDecision.Denied, IpAllowlist.Decide(null, true, Campus, false, []));

    [Fact]
    public void IPv6_works_in_both_lists() =>
        Assert.Equal(IpDecision.AllowedUser, IpAllowlist.Decide(IPAddress.Parse("2001:db8::42"), true, Campus, true, ["2001:db8::/64"]));
}
