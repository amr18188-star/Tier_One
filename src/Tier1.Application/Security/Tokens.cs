using System.Security.Cryptography;
using System.Text;

namespace Tier1.Application.Security;

/// <summary>رموز عشوائية عالية العشوائية وبصماتها؛ القاعدة تحفظ البصمة فقط.</summary>
public static class Tokens
{
    private const string PasswordAlphabet = "ABCDEFGHJKMNPQRSTVWXYZabcdefghjkmnpqrstvwxyz23456789";

    public static string NewToken(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>كلمة مرور مؤقتة مقروءة بطول 16 (نحو 90 بت)، تعرض مرة واحدة ويلزم تغييرها عند أول دخول.</summary>
    public static string NewTemporaryPassword()
    {
        Span<char> chars = stackalloc char[16];
        for (var i = 0; i < chars.Length; i++) chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        return new string(chars);
    }
}
