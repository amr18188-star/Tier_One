using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Tier1.Domain.Identity;
using Tier1.Infrastructure.Security;

namespace Tier1.Tests;

/// <summary>
/// ADR-0021: the data every account carries: both names, the national ID and the mobile number.
/// The numbers here are made up for the tests; none is a real person's.
/// </summary>
public class AccountDataTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    /// <summary>AC-D1: a national ID is 14 digits: the century, a real birth date not in the future, and a governorate code.</summary>
    [Theory]
    [InlineData("28505150112345", true)]   // born 15 May 1985, Cairo
    [InlineData("30102298800017", false)]  // 29 Feb 2001 is not a date
    [InlineData("30002298800017", true)]   // 29 Feb 2000 is
    [InlineData("29912312112345", true)]   // Giza
    [InlineData("2850515011234", false)]   // 13 digits
    [InlineData("285051501123456", false)] // 15 digits
    [InlineData("18505150112345", false)]  // century 1
    [InlineData("48505150112345", false)]  // century 4
    [InlineData("28513150112345", false)]  // month 13
    [InlineData("28505320112345", false)]  // day 32
    [InlineData("28505150512345", false)]  // governorate 05 does not exist
    [InlineData("32612010112345", false)]  // born after today
    [InlineData("2850515011234x", false)]
    public void AC_D1_a_national_id_is_checked_by_its_parts(string id, bool valid) => Assert.Equal(valid, NationalIds.IsValid(id, Today));

    /// <summary>AC-D1: digits typed in Arabic-Indic form, with spaces or dashes, are read as the same number; nothing typed is nothing.</summary>
    [Fact]
    public void AC_D1_a_national_id_is_read_in_any_digit_form()
    {
        Assert.Equal("28505150112345", NationalIds.Normalize(" ٢٨٥٠٥١٥٠١١٢٣٤٥ "));
        Assert.Equal("28505150112345", NationalIds.Normalize("2850515-0112345"));
        Assert.Null(NationalIds.Normalize("  "));
        Assert.Null(NationalIds.Normalize(null));
    }

    /// <summary>AC-D2: after entry only the last four digits are shown or logged.</summary>
    [Fact]
    public void AC_D2_a_national_id_is_masked_to_its_last_four_digits()
    {
        Assert.Equal("**********2345", NationalIds.Mask("28505150112345"));
        Assert.Equal("***", NationalIds.Mask("123"));
    }

    /// <summary>AC-D3: a mobile number is Egyptian (010, 011, 012, 015) and kept in one form however it is typed.</summary>
    [Theory]
    [InlineData("01012345678", "01012345678")]
    [InlineData("0111 234 5678", "01112345678")]
    [InlineData("+20 12 1234 5678", "01212345678")]
    [InlineData("00201512345678", "01512345678")]
    [InlineData("٠١٠١٢٣٤٥٦٧٨", "01012345678")]
    [InlineData("01312345678", null)]   // 013 is not a mobile prefix
    [InlineData("0225551234", null)]    // a landline
    [InlineData("0101234567", null)]    // 10 digits
    [InlineData("010123456789", null)]  // 12 digits
    [InlineData("", null)]
    public void AC_D3_a_mobile_number_is_egyptian_and_kept_in_one_form(string typed, string? kept) => Assert.Equal(kept, MobileNumbers.Normalize(typed));

    /// <summary>AC-D3: the English name is in Latin letters.</summary>
    [Theory]
    [InlineData("Amr Elsayed Mahmoud", true)]
    [InlineData("Mary-Ann O'Neil Jr.", true)]
    [InlineData("عمرو السيد", false)]
    [InlineData("Amr السيد", false)]
    [InlineData("123", false)]
    public void AC_D3_the_english_name_is_in_latin_letters(string name, bool valid) => Assert.Equal(valid, EnglishNames.IsValid(name));

    /// <summary>AC-D8: an account names what it lacks of the required data, and nothing once it is complete.</summary>
    [Fact]
    public void AC_D8_an_account_names_its_missing_data()
    {
        var old = new AppUser { Email = "old@tier1.local", FullNameAr = "حساب قديم" };
        Assert.Equal(["fullNameEn", "nationalId", "phone"], old.MissingData);
        var complete = new AppUser { Email = "new@tier1.local", FullNameAr = "حساب جديد", FullNameEn = "New Account", NationalIdProtected = "x", NationalIdHash = "y", Phone = "01012345678" };
        Assert.Empty(complete.MissingData);
    }

    private static NationalIdProtector Protector(byte first = 1) =>
        new(new EphemeralDataProtectionProvider(), Options.Create(new PersonalDataOptions { IndexKey = Convert.ToBase64String(Enumerable.Range(first, 32).Select(i => (byte)i).ToArray()) }));

    /// <summary>AC-D9: what is stored of a national ID does not hold the number, differs on every save, and is read back by the keys.</summary>
    [Fact]
    public void AC_D9_a_national_id_is_stored_encrypted()
    {
        var p = Protector();
        const string id = "28505150112345";
        var (first, second) = (p.Protect(id), p.Protect(id));
        Assert.DoesNotContain(id, first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(first.Length <= 512, $"The stored value fits its column ({first.Length}).");
        Assert.Equal(id, p.Unprotect(first));
        Assert.Equal(id, p.Unprotect(second));
        // Other keys (a lost or replaced key ring) cannot read it, and say so instead of throwing.
        Assert.Null(Protector().Unprotect(first));
        Assert.Null(p.Unprotect("not a protected value"));
    }

    /// <summary>
    /// AC-D10: the fingerprint is the same for the same number (so the unique index holds), different for another number, and cannot
    /// be computed without the key: another key gives another fingerprint, and it is not a plain hash of the number.
    /// </summary>
    [Fact]
    public void AC_D10_the_fingerprint_is_stable_and_keyed()
    {
        const string id = "28505150112345";
        var print = Protector().Fingerprint(id);
        Assert.Equal(print, Protector().Fingerprint(id));
        Assert.Equal(64, print.Length);
        Assert.Matches("^[0-9a-f]{64}$", print);
        Assert.NotEqual(print, Protector().Fingerprint("28505150112346"));
        Assert.NotEqual(print, Protector(first: 2).Fingerprint(id));
        var plain = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        Assert.NotEqual(plain, print);
        Assert.DoesNotContain(id, print, StringComparison.Ordinal);
    }

    /// <summary>AC-D10: the fingerprint key is at least 32 bytes of Base64; without one no national ID is saved.</summary>
    [Fact]
    public void AC_D10_the_fingerprint_needs_its_key()
    {
        Assert.Null(PersonalDataKey.FromConfig(null));
        Assert.Null(PersonalDataKey.FromConfig("  "));
        Assert.Null(PersonalDataKey.FromConfig(Convert.ToBase64String(new byte[16])));
        Assert.Null(PersonalDataKey.FromConfig("not base64 !"));
        Assert.Equal(32, PersonalDataKey.FromConfig(Convert.ToBase64String(new byte[32]))!.Length);
        var keyless = new NationalIdProtector(new EphemeralDataProtectionProvider(), Options.Create(new PersonalDataOptions()));
        Assert.Throws<InvalidOperationException>(() => keyless.Fingerprint("28505150112345"));
    }
}
