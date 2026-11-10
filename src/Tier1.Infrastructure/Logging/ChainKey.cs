using System.Security.Cryptography;

namespace Tier1.Infrastructure.Logging;

/// <summary>
/// مفتاح سلسلة السجلات (D7): سر خارج القاعدتين، ومعرّفه أول 8 خانات من بصمته، يكتب مع كل حدث
/// فيعرف الفحص بأي مفتاح يتحقق، ويكشف حدثًا بمفتاح غير معروف.
/// </summary>
public sealed class ChainKey
{
    private ChainKey(byte[] secret)
    {
        Secret = secret;
        Id = Convert.ToHexString(SHA256.HashData(secret))[..8].ToLowerInvariant();
    }

    public byte[] Secret { get; }

    public string Id { get; }

    public string Alg => ChainHash.HmacAlg + ":" + Id;

    /// <summary>من نص Base64 (32 بايت على الأقل)؛ null إن لم يضبط.</summary>
    public static ChainKey? FromConfig(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        var bytes = Convert.FromBase64String(base64.Trim().Replace('-', '+').Replace('_', '/').PadRight((base64.Trim().Length + 3) / 4 * 4, '='));
        if (bytes.Length < 32) throw new InvalidOperationException("MongoLogs:ChainKey must be at least 32 bytes (Base64).");
        return new ChainKey(bytes);
    }

    public static ChainKey FromBytes(byte[] secret) => new(secret);
}
