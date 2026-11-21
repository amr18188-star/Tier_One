using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;

namespace Tier1.Application.Access;

public sealed record IpEntryDto(Guid Id, string Value, string? Label, DateTimeOffset CreatedAt, uint Version);

/// <param name="Enabled">The list is enforced (it then applies once it has an entry).</param>
/// <param name="YourAddress">The address this request came from, as the server sees it behind the trusted proxies.</param>
public sealed record IpAllowlistDto(bool Enabled, IReadOnlyList<IpEntryDto> Entries, string? YourAddress);

public sealed record SaveIpEntryRequest(string Value, string? Label, uint? Version);

public sealed record SetIpRestrictionRequest(bool Enabled, string Reason);

public sealed record RemoveIpEntryRequest(string Reason);

/// <summary>
/// ADR-0016: the global IP allowlist and each user's own list (adm.security). Every change is recorded and bumps the access
/// version, so all API instances apply it within seconds. A change to the global list that would shut out the administrator
/// making it is refused (ip_lockout); a user's own list is managed by another administrator (SOD-9), never by its owner.
/// </summary>
public sealed class IpAllowlistService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit,
    IamPolicy policy, IIamLock iamLock, NetworkAccessService network)
{
    private const string Feature = Features.Security;

    // ----- the global list

    public async Task<IpAllowlistDto> GlobalAsync(CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var on = await db.AccessStates.AsNoTracking().Where(s => s.Id == 1).Select(s => s.GlobalIpRestricted).FirstOrDefaultAsync(ct);
        return new IpAllowlistDto(on, await EntriesAsync(null, ct), current.Ip);
    }

    public Task<IpAllowlistDto> SetGlobalAsync(SetIpRestrictionRequest req, CancellationToken ct) => ChangeGlobalAsync(Cap.Edit, async () =>
    {
        var why = Require.Reason(req.Reason);
        var state = await db.AccessStates.FirstAsync(s => s.Id == 1, ct);
        if (state.GlobalIpRestricted == req.Enabled) return;
        state.GlobalIpRestricted = req.Enabled;
        audit.Record(Event(req.Enabled ? "security.ip_global_enable" : "security.ip_global_disable",
            req.Enabled ? "تشغيل قيد عناوين IP العام" : "إيقاف قيد عناوين IP العام",
            req.Enabled ? "Turned on the global IP restriction" : "Turned off the global IP restriction", null, "E") with
        {
            Changes = [new FieldChange("enabled", req.Enabled ? "false" : "true", req.Enabled ? "true" : "false")],
            Reason = why,
        });
    }, ct);

    public Task<IpAllowlistDto> AddGlobalAsync(SaveIpEntryRequest req, CancellationToken ct) =>
        ChangeGlobalAsync(Cap.Add, () => AddEntryAsync(null, null, req, ct), ct);

    public Task<IpAllowlistDto> UpdateGlobalAsync(Guid entryId, SaveIpEntryRequest req, CancellationToken ct) =>
        ChangeGlobalAsync(Cap.Edit, () => UpdateEntryAsync(null, null, entryId, req, ct), ct);

    public Task<IpAllowlistDto> RemoveGlobalAsync(Guid entryId, RemoveIpEntryRequest req, CancellationToken ct) =>
        ChangeGlobalAsync(Cap.Delete, () => RemoveEntryAsync(null, null, entryId, req, ct), ct);

    /// <summary>A change to the global list, refused if it would shut out the administrator making it.</summary>
    private async Task<IpAllowlistDto> ChangeGlobalAsync(Cap cap, Func<Task> change, CancellationToken ct)
    {
        await access.RequireAsync(Feature, cap, ct);
        await using (var tx = await iamLock.BeginAsync(ct))
        {
            await change();
            await db.SaveChangesAsync(ct);
            if (current.UserId is { } me && await network.DecideFreshAsync(me, current.Ip, ct) == IpDecision.Denied)
                throw new DomainException("ip_lockout",
                    $"هذا التغيير يمنعك أنت من الدخول: عنوانك الحالي {current.Ip ?? "؟"} لن يكون مسموحًا. أضف عنوانك أولًا، ثم أعد المحاولة.",
                    $"This change would shut you out: your current address {current.Ip ?? "?"} would not be allowed. Add your address first, then retry.", 422);
            await tx.CommitAsync(ct);
        }
        await access.BumpVersionAsync(ct);
        return await GlobalAsync(ct);
    }

    // ----- a user's own list

    public async Task<IpAllowlistDto> UserAsync(Guid userId, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var on = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (bool?)u.IpRestricted).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound();
        return new IpAllowlistDto(on, await EntriesAsync(userId, ct), null);
    }

    public Task<IpAllowlistDto> SetUserAsync(Guid userId, SetIpRestrictionRequest req, CancellationToken ct) =>
        ChangeUserAsync(userId, Cap.Edit, "security.ip_user_switch", user =>
        {
            var why = Require.Reason(req.Reason);
            if (user.IpRestricted == req.Enabled) return Task.CompletedTask;
            user.IpRestricted = req.Enabled;
            audit.Record(Event(req.Enabled ? "security.ip_user_enable" : "security.ip_user_disable",
                req.Enabled ? "تشغيل قيد عناوين IP لحساب" : "إيقاف قيد عناوين IP لحساب",
                req.Enabled ? "Turned on the IP restriction of an account" : "Turned off the IP restriction of an account", user, "E") with
            {
                Changes = [new FieldChange("ipRestricted", req.Enabled ? "false" : "true", req.Enabled ? "true" : "false")],
                Reason = why,
            });
            return Task.CompletedTask;
        }, ct);

    public Task<IpAllowlistDto> AddUserAsync(Guid userId, SaveIpEntryRequest req, CancellationToken ct) =>
        ChangeUserAsync(userId, Cap.Add, "security.ip_rule_add", user => AddEntryAsync(userId, user, req, ct), ct);

    public Task<IpAllowlistDto> UpdateUserAsync(Guid userId, Guid entryId, SaveIpEntryRequest req, CancellationToken ct) =>
        ChangeUserAsync(userId, Cap.Edit, "security.ip_rule_update", user => UpdateEntryAsync(userId, user, entryId, req, ct), ct);

    public Task<IpAllowlistDto> RemoveUserAsync(Guid userId, Guid entryId, RemoveIpEntryRequest req, CancellationToken ct) =>
        ChangeUserAsync(userId, Cap.Delete, "security.ip_rule_delete", user => RemoveEntryAsync(userId, user, entryId, req, ct), ct);

    /// <summary>The account is managed by another administrator, and one holding a reserved role only by a super administrator.</summary>
    private async Task<IpAllowlistDto> ChangeUserAsync(Guid userId, Cap cap, string action, Func<AppUser, Task> change, CancellationToken ct)
    {
        await access.RequireAsync(Feature, cap, ct);
        var user = await policy.LoadManagedAsync(userId, action, Feature, ct);
        await change(user);
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
        return await UserAsync(userId, ct);
    }

    // ----- entries of either list

    private async Task AddEntryAsync(Guid? userId, AppUser? user, SaveIpEntryRequest req, CancellationToken ct)
    {
        var cidr = NetworkRules.NormalizeEntry(req.Value);
        await EnsureNewAsync(userId, cidr, null, ct);
        var entry = new IpAllowRule { UserId = userId, Cidr = cidr, Label = Require.Optional(req.Label, 200), CreatedAt = clock.UtcNow, CreatedBy = current.UserId };
        db.IpAllowRules.Add(entry);
        audit.Record(EntryEvent("security.ip_rule_add", "إضافة عنوان IP مسموح", "Added an allowed IP address", user, "A") with
        {
            Changes = [new FieldChange("value", null, cidr), new FieldChange("label", null, entry.Label)],
            Details = new { list = userId is null ? "global" : "user" },
        });
    }

    private async Task UpdateEntryAsync(Guid? userId, AppUser? user, Guid entryId, SaveIpEntryRequest req, CancellationToken ct)
    {
        var entry = await db.IpAllowRules.FirstOrDefaultAsync(r => r.Id == entryId && r.UserId == userId, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, entry, Concurrency.Require(req.Version));
        var cidr = NetworkRules.NormalizeEntry(req.Value);
        var label = Require.Optional(req.Label, 200);
        if (cidr != entry.Cidr) await EnsureNewAsync(userId, cidr, entryId, ct);
        var changes = new List<FieldChange>();
        if (cidr != entry.Cidr) changes.Add(new FieldChange("value", entry.Cidr, cidr));
        if (label != entry.Label) changes.Add(new FieldChange("label", entry.Label, label));
        if (changes.Count == 0) return;
        entry.Cidr = cidr;
        entry.Label = label;
        audit.Record(EntryEvent("security.ip_rule_update", "تعديل عنوان IP مسموح", "Updated an allowed IP address", user, "E") with
        {
            Changes = changes,
            Details = new { list = userId is null ? "global" : "user" },
        });
    }

    private async Task RemoveEntryAsync(Guid? userId, AppUser? user, Guid entryId, RemoveIpEntryRequest req, CancellationToken ct)
    {
        var why = Require.Reason(req.Reason);
        var entry = await db.IpAllowRules.FirstOrDefaultAsync(r => r.Id == entryId && r.UserId == userId, ct) ?? throw DomainException.NotFound();
        db.IpAllowRules.Remove(entry);
        audit.Record(EntryEvent("security.ip_rule_delete", "حذف عنوان IP مسموح", "Removed an allowed IP address", user, "D") with
        {
            Changes = [new FieldChange("value", entry.Cidr, null)],
            Reason = why,
            Details = new { list = userId is null ? "global" : "user" },
        });
    }

    private async Task EnsureNewAsync(Guid? userId, string cidr, Guid? self, CancellationToken ct)
    {
        if (await db.IpAllowRules.AnyAsync(r => r.UserId == userId && r.Cidr == cidr && r.Id != self, ct))
            throw new DomainException("duplicate_ip", $"العنوان {cidr} موجود في هذه القائمة.", $"{cidr} is already in this list.", 409) { Field = "value" };
    }

    private async Task<IReadOnlyList<IpEntryDto>> EntriesAsync(Guid? userId, CancellationToken ct) =>
        await db.IpAllowRules.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.CreatedAt)
            .Select(r => new IpEntryDto(r.Id, r.Cidr, r.Label, r.CreatedAt, r.Version)).ToListAsync(ct);

    private static AuditEntry EntryEvent(string action, string ar, string en, AppUser? user, string cap) =>
        Event(action, user is null ? ar + " (القائمة العامة)" : ar + " لحساب", user is null ? en + " (global list)" : en + " for an account", user, cap);

    private static AuditEntry Event(string action, string ar, string en, AppUser? user, string cap) =>
        AuditEvents.Security(action, Feature, ar, en) with
        {
            Permission = $"{Feature}:{cap}",
            Target = user is null ? new AuditTarget("IpAllowlist", "global", "global") : new AuditTarget("User", user.Id.ToString(), user.Email),
        };
}
