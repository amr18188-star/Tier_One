using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Security;

public sealed class PersonalDataOptions
{
    /// <summary>مفتاح بصمة الرقم القومي (32 بايت على الأقل، Base64). سر خارج القاعدة؛ تغييره يبطل البصمات المحفوظة.</summary>
    public string? IndexKey { get; set; }
}

/// <summary>مفتاح بصمة الرقم القومي من الإعدادات (ADR-0021 DEC-D5).</summary>
public static class PersonalDataKey
{
    public const int MinBytes = 32;

    /// <summary>بايتات المفتاح من نص Base64 (32 بايت على الأقل)؛ null إن لم يضبط أو كان أقصر أو غير صالح.</summary>
    public static byte[]? FromConfig(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            return bytes.Length >= MinBytes ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// ADR-0021 DEC-D5: الرقم القومي مشفر في القاعدة بمفاتيح Data Protection المحفوظة خارجها (كأسرار التحقق بخطوتين، بغرض مستقل)،
/// وبصمته HMAC-SHA256 بمفتاح من الإعدادات. التشفير يختلف ناتجه في كل مرة، فلا يصلح لفهرس فريد؛ البصمة ثابتة للرقم نفسه فتمنع
/// تكراره، ولا تحسب دون المفتاح، فلا يستخرج الرقم من القاعدة أو نسخها بتجربة الأرقام الممكنة.
/// </summary>
public sealed class NationalIdProtector(IDataProtectionProvider provider, IOptions<PersonalDataOptions> options) : INationalIdProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Tier1.NationalId.v1");

    private readonly Lazy<byte[]> _indexKey = new(() => PersonalDataKey.FromConfig(options.Value.IndexKey)
        ?? throw new InvalidOperationException($"PersonalData:IndexKey (>= {PersonalDataKey.MinBytes} bytes, Base64) is required to save a national ID."));

    public string Protect(string nationalId) => _protector.Protect(nationalId);

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public string Fingerprint(string nationalId) =>
        Convert.ToHexString(HMACSHA256.HashData(_indexKey.Value, Encoding.UTF8.GetBytes(nationalId))).ToLowerInvariant();
}
