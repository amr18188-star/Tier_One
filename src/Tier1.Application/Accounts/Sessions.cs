using Tier1.Application.Abstractions;
using Tier1.Application.Security;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Tier1.Application.Accounts;

public sealed class AuthOptions
{
    /// <summary>مهلة الخمول بالدقائق.</summary>
    public int IdleMinutes { get; set; } = 30;

    public int AbsoluteHours { get; set; } = 12;

    /// <summary>المحاولات الفاشلة من عنوان واحد قبل حجبه عن الحساب مدة القفل.</summary>
    public int MaxFailedLogins { get; set; } = 5;

    /// <summary>المحاولات الفاشلة من كل العناوين قبل قفل الحساب نفسه؛ أعلى من حد العنوان فلا يقفله أحد من مكان واحد.</summary>
    public int MaxFailedLoginsPerAccount { get; set; } = 20;

    /// <summary>
    /// Failed sign-ins from one address across all accounts (unknown emails included) within AddressWindowMinutes before the
    /// address is blocked for AddressBlockMinutes: password spraying and credential stuffing. High, because a whole campus can sit
    /// behind one address; an account that signed in from that address within KnownAddressDays is not blocked by it.
    /// </summary>
    public int MaxFailedLoginsPerAddress { get; set; } = 100;

    public int AddressWindowMinutes { get; set; } = 15;

    public int AddressBlockMinutes { get; set; } = 15;

    /// <summary>Days an address stays known for an account after a successful sign-in from it.</summary>
    public int KnownAddressDays { get; set; } = 30;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>محاولات رمز TOTP الفاشلة قبل قفل التحقق الإضافي.</summary>
    public int MaxMfaFailures { get; set; } = 5;

    public int MfaLockoutMinutes { get; set; } = 15;

    public string TotpIssuer { get; set; } = "Tier1";

    /// <summary>أدوار يفرض عليها التحقق بخطوتين (NFR01: الحسابات الحساسة).</summary>
    public List<string> MfaRoles { get; set; } = ["SA", "SYS", "AUD", "PRES", "HC", "PM"];

    /// <summary>إيقاف التحقق بخطوتين للتطوير فقط؛ أي بيئة غير التطوير ترفض البدء به.</summary>
    public bool RequireMfa { get; set; } = true;

    public bool MfaEnforcedFor(string roleCode) => RequireMfa && MfaRoles.Contains(roleCode);
}

/// <summary>MfaVerified: هل أكملت هذه الجلسة التحقق بخطوتين (تطلبه الأدوار الحساسة حتى لو أسندت بعد الدخول).</summary>
public sealed record SessionPrincipal(Guid SessionId, Guid UserId, string DisplayName, string Language, bool MfaPending, bool MustChangePassword, bool MfaVerified);

public interface ISessionService
{
    Task<(string Token, UserSession Session)> CreateAsync(AppUser user, bool mfaRequired, CancellationToken ct);

    /// <param name="passive">A background poll (FE-2): it reads the session without extending the idle window.</param>
    Task<SessionPrincipal?> ValidateAsync(string token, CancellationToken ct, bool passive = false);

    Task MarkMfaVerifiedAsync(Guid sessionId, CancellationToken ct);

    /// <summary>يمسح خبيئة الجلسة فتقرأ حالة الحساب من جديد (بعد تغيير كلمة المرور مثلًا).</summary>
    Task InvalidateAsync(Guid sessionId, CancellationToken ct);

    Task RevokeAsync(Guid sessionId, CancellationToken ct);

    Task<int> RevokeAllAsync(Guid userId, CancellationToken ct);

    /// <summary>رمز جديد للجلسة نفسها بعد رفع صلاحيتها (التحقق بخطوتين، تغيير كلمة المرور): الرمز السابق لا يعود صالحًا.</summary>
    Task<string> RotateAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>
/// جلسات معتمة: الرمز عشوائي والقاعدة تحفظ بصمته فقط. التحقق يخبأ 30 ثانية؛ الإلغاء يمسح الخبيئة فورًا لهذه النسخة.
/// </summary>
public sealed class SessionService(IIdentityStore db, IClock clock, ICurrentUser current, IMemoryCache cache, IOptions<AuthOptions> options) : ISessionService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task<(string Token, UserSession Session)> CreateAsync(AppUser user, bool mfaRequired, CancellationToken ct)
    {
        var token = Tokens.NewToken();
        var now = clock.UtcNow;
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = Tokens.Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddHours(options.Value.AbsoluteHours),
            MfaRequired = mfaRequired,
            Ip = current.Ip,
            UserAgent = current.UserAgent is { Length: > 300 } ua ? ua[..300] : current.UserAgent,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return (token, session);
    }

    public async Task<SessionPrincipal?> ValidateAsync(string token, CancellationToken ct, bool passive = false)
    {
        var hash = Tokens.Hash(token);
        if (cache.TryGetValue<SessionPrincipal>(Key(hash), out var cached)) return cached;
        var now = clock.UtcNow;
        var idle = TimeSpan.FromMinutes(options.Value.IdleMinutes);
        var row = await db.Sessions.AsNoTracking()
            .Where(s => s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now)
            .Join(db.Users.Where(u => u.Active), s => s.UserId, u => u.Id, (s, u) => new { Session = s, User = u })
            .FirstOrDefaultAsync(ct);
        if (row is null || now - row.Session.LastSeenAt > idle) return null;
        var s = row.Session;
        var principal = new SessionPrincipal(s.Id, row.User.Id, Lang.Name(row.User.FullNameAr, row.User.FullNameEn), row.User.PreferredLanguage,
            s.MfaRequired && s.MfaVerifiedAt is null, row.User.MustChangePassword, s.MfaVerifiedAt is not null);
        // FE-2: a background poll neither slides the idle window nor fills the cache (the next real request must still slide it).
        if (passive) return principal;
        if (now - s.LastSeenAt > TimeSpan.FromMinutes(1))
            await db.Sessions.Where(x => x.Id == s.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, now), ct);
        cache.Set(Key(hash), principal, CacheFor);
        return principal;
    }

    public async Task MarkMfaVerifiedAsync(Guid sessionId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.TokenHash).FirstOrDefaultAsync(ct);
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.MfaVerifiedAt, now), ct);
        if (hash is not null) cache.Remove(Key(hash));
    }

    public async Task InvalidateAsync(Guid sessionId, CancellationToken ct)
    {
        var hash = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.TokenHash).FirstOrDefaultAsync(ct);
        if (hash is not null) cache.Remove(Key(hash));
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.TokenHash).FirstOrDefaultAsync(ct);
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
        if (hash is not null) cache.Remove(Key(hash));
    }

    public async Task<int> RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hashes = await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).Select(s => s.TokenHash).ToListAsync(ct);
        var count = await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
        foreach (var h in hashes) cache.Remove(Key(h));
        return count;
    }

    public async Task<string> RotateAsync(Guid sessionId, CancellationToken ct)
    {
        var old = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.TokenHash).FirstOrDefaultAsync(ct);
        var token = Tokens.NewToken();
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.TokenHash, Tokens.Hash(token)), ct);
        if (old is not null) cache.Remove(Key(old));
        return token;
    }

    private static string Key(string hash) => "session:" + hash;
}
