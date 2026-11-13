using Tier1.Application.Abstractions;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Tier1.Application.Access;

/// <summary>
/// رقم إصدار الصلاحيات المعروف لهذه النسخة من التطبيق. يرفعه التعديل المحلي فورًا،
/// ويحدثه من القاعدة مراقب دوري كل بضع ثوانٍ، فيسري التعديل على كل النسخ خلال دقيقة على الأكثر (AC11).
/// </summary>
public sealed class AccessVersion
{
    private long _version;

    public long Current => Volatile.Read(ref _version);

    public void Set(long version)
    {
        long seen;
        do
        {
            seen = Volatile.Read(ref _version);
            if (version <= seen) return;
        }
        while (Interlocked.CompareExchange(ref _version, version, seen) != seen);
    }
}

public interface IAccessCache
{
    /// <summary>أدوار المستخدم من آخر حساب في الذاكرة، للسجل فقط (بلا استعلام).</summary>
    IReadOnlyList<string> CachedRoleCodes(Guid userId);
}

public interface IAccessService : IAccessCache
{
    Task<EffectiveAccess> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>صلاحيات المستخدم الحالي؛ لا شيء لمن لم يسجل دخوله.</summary>
    Task<EffectiveAccess> CurrentAsync(CancellationToken ct);

    Task<EffectiveAccess> RequireAsync(string feature, Cap cap, CancellationToken ct);

    Task<EffectiveAccess> RequireSpecialAsync(string feature, string special, CancellationToken ct);

    /// <summary>يرفع رقم الإصدار العام بعد أي تعديل في الأدوار أو المنح أو الإسناد.</summary>
    Task BumpVersionAsync(CancellationToken ct);
}

public sealed class AccessService(IAppDb db, IClock clock, ICurrentUser current, IMemoryCache cache, AccessVersion version) : IAccessService
{
    /// <summary>الحساب يخبأ دقيقة على الأكثر، فينتهي الإسناد المؤقت في موعده حتى بلا تعديل.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    public async Task<EffectiveAccess> GetAsync(Guid userId, CancellationToken ct)
    {
        var key = $"access:{userId}:{version.Current}";
        if (cache.TryGetValue<EffectiveAccess>(key, out var hit) && hit is not null) return hit;
        var now = clock.UtcNow;
        var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Active, ct);
        EffectiveAccess access;
        if (!active)
        {
            access = EffectiveAccess.None(userId);
        }
        else
        {
            var assignments = await db.UserRoles.AsNoTracking()
                .Where(a => a.UserId == userId && a.RevokedAt == null && a.ApprovedAt != null && a.ValidFrom <= now && (a.ValidTo == null || a.ValidTo > now))
                .ToListAsync(ct);
            var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
            var roles = await db.Roles.AsNoTracking().Include(r => r.Grants).Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            var overrides = await db.UserOverrides.AsNoTracking()
                .Where(o => o.UserId == userId && o.RevokedAt == null && o.ValidFrom <= now && (o.ValidTo == null || o.ValidTo > now))
                .ToListAsync(ct);
            var wide = assignments.Any(a => a.ScopeValues.Count > 0)
                ? await db.OrgUnits.AsNoTracking().Where(u => u.ViewsWholeUniversity && u.Active).Select(u => u.Code).ToListAsync(ct)
                : [];
            access = EffectiveAccessCalculator.Compute(
                userId,
                assignments.Where(a => roles.ContainsKey(a.RoleId)).Select(a => new AssignedRole(roles[a.RoleId].Code, roles[a.RoleId].IsLocked, roles[a.RoleId].Grants, a)),
                overrides,
                now,
                wide);
        }
        cache.Set(key, access, CacheFor);
        cache.Set(RolesKey(userId), access.RoleCodes, TimeSpan.FromHours(1));
        return access;
    }

    public async Task<EffectiveAccess> CurrentAsync(CancellationToken ct) =>
        current.UserId is { } uid ? await GetAsync(uid, ct) : EffectiveAccess.None(Guid.Empty);

    public async Task<EffectiveAccess> RequireAsync(string feature, Cap cap, CancellationToken ct)
    {
        var access = await CurrentAsync(ct);
        return access.Has(feature, cap) ? access : throw DomainException.Forbidden();
    }

    public async Task<EffectiveAccess> RequireSpecialAsync(string feature, string special, CancellationToken ct)
    {
        var access = await CurrentAsync(ct);
        return access.HasSpecial(feature, special) ? access : throw DomainException.Forbidden();
    }

    /// <summary>يستدعى بعد إتمام المعاملة، فلا تخبأ صلاحيات ما قبل التعديل تحت الرقم الجديد.</summary>
    public async Task BumpVersionAsync(CancellationToken ct)
    {
        await db.AccessStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct);
        var now = await db.AccessStates.AsNoTracking().Where(s => s.Id == 1).Select(s => s.Version).FirstAsync(ct);
        version.Set(now);
    }

    public IReadOnlyList<string> CachedRoleCodes(Guid userId) =>
        cache.TryGetValue<IReadOnlyList<string>>(RolesKey(userId), out var codes) && codes is not null ? codes : [];

    private static string RolesKey(Guid userId) => "roles:" + userId;
}
