using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Application.Security;
using Tier1.Domain.Common;

namespace Tier1.Tests;

public class SecurityTests
{
    // RFC 6238طŒ ط§ظ„ظ…ظ„ط­ظ‚ B: ط§ظ„ط³ط± "12345678901234567890" ط¨ط¹ط¯ Base32طŒ ظˆط§ظ„ظˆظ‚طھ 59 ط«ط§ظ†ظٹط© â‡’ 94287082 (ط¢ط®ط± ط³طھ ط®ط§ظ†ط§طھ 287082).
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public void Totp_matches_the_rfc_vector() =>
        Assert.Equal("287082", Totp.Code(RfcSecret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(59))));

    [Fact]
    public void Totp_rejects_reuse_of_an_accepted_step()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var code = Totp.Code(RfcSecret, Totp.StepAt(now));
        var step = Totp.Verify(RfcSecret, code, now, lastAcceptedStep: 0);
        Assert.NotNull(step);
        Assert.Null(Totp.Verify(RfcSecret, code, now, step!.Value));
        Assert.Null(Totp.Verify(RfcSecret, "12345", now, 0));
    }

    [Fact]
    public void Mfa_roles_are_enforced_unless_switched_off()
    {
        var on = new AuthOptions();
        Assert.True(on.MfaEnforcedFor("HC"));
        Assert.False(on.MfaEnforcedFor("DE"));
        Assert.False(new AuthOptions { RequireMfa = false }.MfaEnforcedFor("HC"));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("planner-password-1")]
    public void Weak_passwords_are_rejected(string password) =>
        Assert.Equal("weak_password", Assert.Throws<DomainException>(() => PasswordPolicy.Ensure(password, "planner@tier1.local")).Code);

    [Fact]
    public void Strong_password_is_accepted() => PasswordPolicy.Ensure("Center-Strong-2026!", "planner@tier1.local");

    [Theory]
    [InlineData("29801011234567", "**********4567")]
    [InlineData("123", "***")]
    [InlineData(null, null)]
    public void Identity_numbers_are_masked_to_the_last_four(string? input, string? expected) =>
        Assert.Equal(expected, Mask.Identity(input));

    [Fact]
    public void Email_is_masked_keeping_the_domain() =>
        Assert.Equal("p******@tier1.local", Mask.Email("planner@tier1.local"));
}
