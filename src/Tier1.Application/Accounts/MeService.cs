using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Tier1.Application.Accounts;

public sealed record MeDto(Guid Id, string Email, string Name, string FullNameAr, string? FullNameEn, string Language, bool MfaEnabled, bool MfaPending, bool MustChangePassword, EffectiveDto Permissions);

public sealed record SessionDto(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? Ip, string? UserAgent, bool Current);

public sealed class MeService(IIdentityStore db, ICurrentUser current, IAccessService access)
{
    public async Task<MeDto> GetAsync(bool mfaPending, CancellationToken ct)
    {
        var uid = current.UserId ?? throw DomainException.Forbidden();
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid, ct) ?? throw DomainException.Forbidden();
        // الجلسة التي تنتظر التحقق بخطوتين لا تحصل على خريطة الصلاحيات بعد.
        var permissions = mfaPending || u.MustChangePassword ? EffectiveDto.From(EffectiveAccess.None(uid)) : EffectiveDto.From(await access.GetAsync(uid, ct));
        return new MeDto(u.Id, u.Email, u.DisplayName, u.FullNameAr, u.FullNameEn, u.PreferredLanguage, u.MfaEnabled, mfaPending, u.MustChangePassword, permissions);
    }

    public async Task SetLanguageAsync(string language, CancellationToken ct)
    {
        var uid = current.UserId ?? throw DomainException.Forbidden();
        var lang = language == "en" ? "en" : "ar";
        await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, lang), ct);
    }

    public async Task<IReadOnlyList<SessionDto>> SessionsAsync(CancellationToken ct)
    {
        var uid = current.UserId ?? throw DomainException.Forbidden();
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Sessions.AsNoTracking().Where(s => s.UserId == uid && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt).Take(20).ToListAsync(ct);
        return rows.Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastSeenAt, s.Ip, s.UserAgent, s.Id == current.SessionId)).ToList();
    }
}
