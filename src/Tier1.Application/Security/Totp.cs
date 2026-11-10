using System.Security.Cryptography;
using System.Text;

namespace Tier1.Application.Security;

/// <summary>TOTP وفق RFC 6238 (HMAC-SHA1، 6 أرقام، 30 ثانية، نافذة ±1).</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Base32(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / 30;

    public static string Code(string base32Secret, long step)
    {
        var key = FromBase32(base32Secret);
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xff);
            step >>= 8;
        }
#pragma warning disable CA5350 // RFC 6238 يفرض HMAC-SHA1 لتوافق تطبيقات المصادقة؛ ليس استخدامًا للتجزئة في الثقة
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>يرجع الخطوة المطابقة إن صح الرمز وكانت أحدث من آخر خطوة مقبولة (منع إعادة الاستخدام).</summary>
    public static long? Verify(string base32Secret, string? code, DateTimeOffset now, long lastAcceptedStep)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = new string(code.Where(char.IsAsciiDigit).ToArray());
        if (normalized.Length != 6) return null;
        var step = StepAt(now);
        for (var delta = -1; delta <= 1; delta++)
        {
            var candidate = step + delta;
            if (candidate <= lastAcceptedStep) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(base32Secret, candidate)), Encoding.ASCII.GetBytes(normalized)))
                return candidate;
        }
        return null;
    }

    public static string OtpAuthUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";

    private static string Base32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] FromBase32(string s)
    {
        var clean = s.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var v = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (v < 0) throw new FormatException("Invalid base32 secret.");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return [.. output];
    }
}
