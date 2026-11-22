using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;

namespace Tier1.Application.Access;

/// <summary>
/// ADR-0016: whether an address may be used by a user, on every request. The rules are read once per access version and kept in
/// memory; every change to them bumps the version, which the other API instances pick up within seconds (AccessVersionPoller).
/// </summary>
public sealed class NetworkAccessService(IIdentityStore db, IMemoryCache cache, AccessVersion version, IAuditLog audit)
{
    private sealed record Rules(bool GlobalOn, string[] Global, Dictionary<Guid, string[]> Restricted);

    /// <summary>The refusal the user sees: which address, and whom to ask.</summary>
    public static DomainException Denied(string? ip) => new("ip_not_allowed",
        $"لا يسمح بالدخول إلى المنظومة من عنوانك الحالي ({ip ?? "غير معروف"}). ادخل من شبكة الجامعة، أو اطلب من مسؤول النظام إضافة عنوانك.",
        $"Access to the system is not allowed from your current address ({ip ?? "unknown"}). Connect from the university network, or ask the system administrator to add your address.", 403);

    /// <summary>The security log entry of a refused address: who, from where, when (the entry's time) and the decision.</summary>
    public static AuditEntry DeniedEvent(AppUser user, string? ip, string surface) =>
        AuditEvents.Security("auth.ip_denied", Access.Features.Security, "رفض وصول من عنوان IP غير مسموح", "Refused access from an IP address that is not allowed") with
        {
            Outcome = "denied",
            Target = new AuditTarget("User", user.Id.ToString(), user.Email),
            ActorOverride = user.Id,
            ActorName = user.DisplayName,
            Details = new { ip, decision = "denied", surface, username = user.Email },
        };

    /// <summary>
    /// A request of a signed-in session from an address that is not allowed: recorded once per session and address every ten
    /// minutes, so an open tab polling from the wrong network does not flood the security log.
    /// </summary>
    public async Task RecordDeniedRequestAsync(Guid userId, Guid sessionId, string? ip, CancellationToken ct)
    {
        var key = $"ip-denied:{sessionId}:{ip}";
        if (cache.TryGetValue(key, out _)) return;
        cache.Set(key, true, TimeSpan.FromMinutes(10));
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null) await audit.RecordNowAsync(DeniedEvent(user, ip, "session"), ct);
    }

    public async Task<IpDecision> DecideAsync(Guid userId, string? ip, CancellationToken ct)
    {
        var rules = await cache.GetOrCreateAsync($"ip-rules:{version.Current}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await LoadAsync(ct);
        });
        return Decide(rules!, userId, ip);
    }

    /// <summary>The same decision from the database as it is now in this transaction (to check a change before it is committed).</summary>
    public async Task<IpDecision> DecideFreshAsync(Guid userId, string? ip, CancellationToken ct) => Decide(await LoadAsync(ct), userId, ip);

    private static IpDecision Decide(Rules rules, Guid userId, string? ip) =>
        IpAllowlist.Decide(NetworkRules.ParseAddress(ip), rules.GlobalOn, rules.Global, rules.Restricted.ContainsKey(userId), rules.Restricted.GetValueOrDefault(userId) ?? []);

    private async Task<Rules> LoadAsync(CancellationToken ct)
    {
        var globalOn = await db.AccessStates.AsNoTracking().Where(s => s.Id == 1).Select(s => s.GlobalIpRestricted).FirstOrDefaultAsync(ct);
        var restricted = await db.Users.AsNoTracking().Where(u => u.IpRestricted).Select(u => u.Id).ToListAsync(ct);
        var rules = await db.IpAllowRules.AsNoTracking().Select(r => new { r.UserId, r.Cidr }).ToListAsync(ct);
        return new Rules(globalOn,
            [.. rules.Where(r => r.UserId == null).Select(r => r.Cidr)],
            restricted.ToDictionary(id => id, id => rules.Where(r => r.UserId == id).Select(r => r.Cidr).ToArray()));
    }
}
