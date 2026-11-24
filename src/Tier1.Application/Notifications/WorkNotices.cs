using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Domain.Access;
using Tier1.Domain.Organization;

namespace Tier1.Application.Notifications;

/// <summary>
/// Who a work notice is for. Either named people (<see cref="Users"/>), or everyone who now holds an action: a capability
/// (<see cref="Feature"/> + <see cref="Cap"/>) or a special (<see cref="Feature"/> + <see cref="Special"/>), with a scope that
/// covers <see cref="UnitId"/> when one is given. Super administrators hold every action through their full grants, so a rule
/// leaves them out unless <see cref="SuperAdmins"/> asks for them alone (the actions only they take). <see cref="Except"/> is
/// never told: the person who acted, or one separation of duties bars from the decision.
/// </summary>
public sealed record WorkAudience(
    IReadOnlyList<Guid>? Users = null, string? Feature = null, Cap? Cap = null, string? Special = null, Guid? UnitId = null,
    bool SuperAdmins = false, IReadOnlyList<Guid>? Except = null);

/// <summary>
/// System integration review 2026-11-24, G-04: work passed to someone else who was not told (a reopen request and its decision,
/// definitions and targets for approval, values ready to transfer, a super-administrator assignment or a matrix change waiting
/// for a second super administrator, a cycle opened, a deadline moved). Raised in the change's own transaction; the recipients
/// are read when the handler runs, from the access they hold then, and each is told in the inbox and by mail.
/// </summary>
public sealed record WorkNotice(string Kind, string Title, string Body, string? Link, WorkAudience To) : Integration.IDomainEvent
{
    public static string EventType => "work.notice";
}

/// <summary>Adds a work notice to the outbox of the caller's unit of work.</summary>
public static class WorkNoticeOutbox
{
    public static void Notify(this Integration.IEventOutbox events, WorkNotice notice, string aggregateType, Guid aggregateId) =>
        events.Add(notice, aggregateType, aggregateId);
}

/// <summary>Turns a <see cref="WorkAudience"/> into the active people it names now.</summary>
public sealed class AudienceResolver(IReadModel read, IAccessService access)
{
    public async Task<List<Recipient>> ResolveAsync(WorkAudience to, CancellationToken ct)
    {
        var except = to.Except?.ToHashSet() ?? [];
        var users = read.Query<Domain.Identity.AppUser>().AsNoTracking().Where(u => u.Active);
        if (to.Users is { Count: > 0 } named)
            return await users.Where(u => named.Contains(u.Id) && !except.Contains(u.Id)).OrderBy(u => u.Email).Select(u => new Recipient(u.Id, u.Email)).ToListAsync(ct);

        var unit = to.UnitId is { } unitId ? await read.Query<OrgUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct) : null;
        var assignments = read.Query<UserRole>().Where(a => a.RevokedAt == null && a.ApprovedAt != null);
        IQueryable<Guid> candidates;
        if (to.SuperAdmins)
        {
            var sa = read.Query<Role>().Where(r => r.Code == PermissionCatalog.SuperAdminRole).Select(r => r.Id);
            candidates = assignments.Where(a => sa.Contains(a.RoleId)).Select(a => a.UserId);
        }
        else if (to.Feature is { } feature)
        {
            // Only the people who could pass the rule: a role or an allowing exception that touches the feature.
            var roles = read.Query<Role>().Where(r => r.Grants.Any(g => g.Feature == feature)).Select(r => r.Id);
            var overrides = read.Query<UserOverride>().Where(o => o.RevokedAt == null && o.Effect == OverrideEffect.Allow && o.Feature == feature).Select(o => o.UserId);
            candidates = assignments.Where(a => roles.Contains(a.RoleId)).Select(a => a.UserId).Union(overrides);
        }
        else return [];

        var people = await users.Where(u => candidates.Contains(u.Id) && !except.Contains(u.Id)).OrderBy(u => u.Email).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        var result = new List<Recipient>();
        foreach (var p in people)
        {
            var a = await access.GetAsync(p.Id, ct);
            if (Holds(a, to, unit)) result.Add(new Recipient(p.Id, p.Email));
        }
        return result;
    }

    /// <summary>Whether this access holds the audience's action on its unit (or anywhere when no unit is named).</summary>
    internal static bool Holds(EffectiveAccess a, WorkAudience to, OrgUnit? unit)
    {
        if (to.SuperAdmins) return a.IsSuperAdmin;
        if (a.IsSuperAdmin || to.Feature is not { } feature) return false;
        if (to.Special is { } special)
            return a.HasSpecial(feature, special) && (unit is null || UnitScope.ForSpecial(a, feature, special).Covers(unit));
        var cap = to.Cap ?? Cap.View;
        return a.Has(feature, cap) && (unit is null || UnitScope.For(a, feature, cap).Covers(unit));
    }
}

/// <summary>Tells the audience of a work notice, after the change that raised it committed.</summary>
public sealed class WorkNoticeNotifications(AudienceResolver audience, INotificationPublisher notifications) : Integration.DomainEventHandler<WorkNotice>
{
    public override string Name => "notify.work_notice";

    protected override async Task HandleAsync(WorkNotice evt, Domain.Integration.DomainEventRecord record, CancellationToken ct)
    {
        foreach (var to in await audience.ResolveAsync(evt.To, ct))
            notifications.Publish(to, evt.Kind, evt.Title, evt.Body, evt.Link);
    }
}
