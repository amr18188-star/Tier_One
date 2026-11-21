using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Tier1.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace Tier1.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>PBKDF2 (HMAC-SHA512، 210 آلاف تكرار كتوصية OWASP) من ASP.NET Core Identity؛ يرقّي التجزئات الأضعف تلقائيًا عند الدخول.</summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private static readonly object Subject = new();

    public string Hash(string password) => Hasher.HashPassword(Subject, password);

    public (bool Valid, bool NeedsRehash) Verify(string hash, string password)
    {
        try
        {
            var result = Hasher.VerifyHashedPassword(Subject, hash, password);
            return (result != PasswordVerificationResult.Failed, result == PasswordVerificationResult.SuccessRehashNeeded);
        }
        catch (FormatException)
        {
            return (false, false);
        }
    }
}

/// <summary>تشفير الأسرار المخزنة بمفاتيح Data Protection المحفوظة خارج القاعدة.</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Tier1.Secrets.v1");

    public string Protect(string plain) => _protector.Protect(plain);

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
}

public sealed class QrCodes : IQrCodes
{
    public string PngDataUri(string content, int pixelsPerModule)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }
}

public static class ClaimNames
{
    public const string UserId = "uid";
    public const string SessionId = "sid";
    public const string Name = "name";
    public const string MfaPending = "mfa_pending";
    public const string MfaVerified = "mfa_verified";
    public const string PasswordChangePending = "password_change_pending";
}

/// <summary>هوية الطلب الحالي؛ IP من الاتصال بعد معالجة الوسيط العكسي الموثوق فقط.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId => accessor.HttpContext is null ? null : Guid.TryParse(User?.FindFirstValue(ClaimNames.UserId), out var id) ? id : null;

    public Guid? SessionId => Guid.TryParse(User?.FindFirstValue(ClaimNames.SessionId), out var id) ? id : null;

    public string? DisplayName => User?.FindFirstValue(ClaimNames.Name);

    // The address after ForwardedHeaders (trusted proxies only). An IPv4 client on a dual-stack socket arrives as ::ffff:a.b.c.d;
    // it is written as IPv4, as the allowlists (ADR-0016), the counters and the logs expect.
    public string? Ip => accessor.HttpContext?.Connection.RemoteIpAddress is { } a ? (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString() : null;

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string TraceId => Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

    public string Channel => accessor.HttpContext is null ? "job" : "web";
}
