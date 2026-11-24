using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Notifications;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

/// <summary>A form changed hands: sent for review, approved a step, returned, or reopened. Raised in the transition's transaction.</summary>
public sealed record FormMoved(Guid SubmissionId, Guid UnitId, Guid CycleId, SubmissionStatus Status, ApprovalStep Step, DateOnly DueOn, string? ReturnNote)
    : Integration.IDomainEvent
{
    public static string EventType => "form.moved";
}

/// <summary>Mails whoever acts next on a moved form (BE-3: outside the request that moved it).</summary>
public sealed class FormMovedNotifications(FormNotifier notifier) : Integration.DomainEventHandler<FormMoved>
{
    public override string Name => "notify.form_moved";

    protected override Task HandleAsync(FormMoved evt, Domain.Integration.DomainEventRecord record, CancellationToken ct) => notifier.MovedAsync(evt, ct);
}

/// <summary>
/// إشعارات مسار النماذج بالبريد: من عليه الإجراء التالي يصله رابط النموذج مباشرة. المستلمون يحسبون من الصلاحيات الفعلية:
/// مراجع الخطوة (إجراؤها الخاص ونطاقه يغطي الجهة)، أو مدخلو البيانات عند الإعادة. الرسالة تضاف بعد ثبوت الانتقال، من معالج حدث form.moved.
/// </summary>
public sealed class FormNotifier(ICollectionStore db, IReadModel read, IAccessService access, INotificationPublisher notifications, AudienceResolver audience)
{
    /// <summary>
    /// Runs in the form.moved handler, after the transition committed. The message describes the form as the event saw it (its step
    /// and note then); the recipients are whoever holds the step's access now. Each gets it in the inbox and by mail.
    /// </summary>
    public async Task MovedAsync(FormMoved s, CancellationToken ct)
    {
        var unit = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstAsync(u => u.Id == s.UnitId, ct);
        var cycle = await db.Cycles.AsNoTracking().FirstAsync(c => c.Id == s.CycleId, ct);
        var (recipients, title, body) = s.Status switch
        {
            SubmissionStatus.InReview => (await ReviewersAsync(s.Step, unit, ct),
                $"نموذج {unit.NameAr} ينتظر مراجعتك — {cycle.Name}",
                $"أرسل نموذج {unit.NameAr} في دورة «{cycle.Name}» وهو الآن عند خطوة «{ApprovalRules.StepName(s.Step)}».\nالموعد النهائي: {s.DueOn:yyyy-MM-dd}"),
            SubmissionStatus.Returned or SubmissionStatus.InProgress => (await EditorsAsync(s.SubmissionId, unit, ct),
                $"أعيد نموذج {unit.NameAr} للتصحيح — {cycle.Name}",
                $"أعيد نموذج {unit.NameAr} في دورة «{cycle.Name}» بالملاحظة:\n{s.ReturnNote}\nالمؤشرات المطلوب تصحيحها مميزة في النموذج."),
            SubmissionStatus.Approved => (await EditorsAsync(s.SubmissionId, unit, ct),
                $"اعتمد نموذج {unit.NameAr} نهائيًا — {cycle.Name}",
                $"اعتمدت لجنة Tier 1 بالجامعة نموذج {unit.NameAr} في دورة «{cycle.Name}». شكرًا لكم."),
            _ => ([], "", ""),
        };
        // Adds the messages only; the event dispatcher saves them together with the record that this handler ran.
        foreach (var to in recipients) notifications.Publish(to, "dc.form", title, body, $"/forms/{s.SubmissionId}");

        // G-04, G-05: a finally approved form gives the university Tier 1 committee values to review and transfer. After a reopen they
        // changed values it may already have transferred, which stay official (and in the scorecard) until it transfers them again.
        if (s.Status == SubmissionStatus.Approved && !cycle.Trial && cycle.Quarter is not null)
        {
            var reopened = await db.ReopenRequests.AsNoTracking().AnyAsync(r => r.SubmissionId == s.SubmissionId && r.Status == ReopenRequestStatus.Approved, ct);
            var (t, b) = reopened
                ? ($"تغيّرت قيم {unit.NameAr} بعد إعادة الفتح — {cycle.Name}",
                   $"اعتُمد نموذج {unit.NameAr} من جديد بعد إعادة فتحه في دورة «{cycle.Name}». ما نُقل من مؤشراته إلى مؤشرات الجامعة يبقى بقيمته القديمة في بطاقة الأداء حتى تنقله من جديد.")
                : ($"قيم {unit.NameAr} جاهزة للمراجعة والنقل — {cycle.Name}",
                   $"اعتُمد نموذج {unit.NameAr} نهائيًا في دورة «{cycle.Name}». راجع مؤشرات الكليات وانقل ما اكتمل إلى مؤشرات الجامعة.");
            foreach (var to in await audience.ResolveAsync(new WorkAudience(Feature: Features.University, Cap: Cap.Edit), ct))
                notifications.Publish(to, "dc.review", t, b, $"/review?cycle={s.CycleId}");
        }
    }

    /// <summary>تذكير يومي بالنماذج المتأخرة أو القريبة من موعدها: لمن عليه الإجراء الآن، مرة في اليوم لكل نموذج.</summary>
    public async Task<int> RemindAsync(DateOnly today, int daysBefore, CancellationToken ct)
    {
        var open = await db.Cycles.AsNoTracking().Where(c => c.Status == CycleStatus.Open).ToDictionaryAsync(c => c.Id, ct);
        var ids = open.Keys.ToList();
        var limit = today.AddDays(daysBefore);
        var forms = await db.Submissions.AsNoTracking().Where(s => ids.Contains(s.CycleId) && s.Status != SubmissionStatus.Approved && s.DueOn <= limit).ToListAsync(ct);
        var units = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var sent = 0;
        foreach (var s in forms)
        {
            var key = $"rem:{s.Id:N}:{today:yyyyMMdd}";
            if (await read.Query<Domain.Notifications.OutgoingMail>().AnyAsync(m => m.DedupKey != null && m.DedupKey.StartsWith(key), ct)) continue;
            var unit = units[s.UnitId];
            var to = s.Status == SubmissionStatus.InReview ? await ReviewersAsync(s.Step, unit, ct) : await EditorsAsync(s.Id, unit, ct);
            var late = s.DueOn < today;
            foreach (var recipient in to)
            {
                notifications.Publish(recipient, "dc.reminder",
                    late ? $"نموذج {unit.NameAr} متأخر عن موعده — {open[s.CycleId].Name}" : $"تذكير: موعد نموذج {unit.NameAr} يقترب — {open[s.CycleId].Name}",
                    $"نموذج {unit.NameAr} عند «{ApprovalRules.StepName(s.Step)}»، والموعد النهائي {s.DueOn:yyyy-MM-dd}{(late ? " وقد مضى" : "")}.",
                    $"/forms/{s.Id}", $"{key}:{recipient.UserId:N}");
                sent++;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    internal async Task<List<Recipient>> ReviewersAsync(ApprovalStep step, OrgUnit unit, CancellationToken ct, bool everyone = false)
    {
        if (step is ApprovalStep.Entry or ApprovalStep.Done) return [];
        var regular = await UsersWhereAsync(Need.Review, a => ApprovalRules.MayReview(a, unit, step, allowDirect: false) && UnitScope.For(a, Features.Forms, Cap.View).Covers(unit), ct, everyone);
        // G-03 (system integration review 2026-11-24): when nobody but a super administrator holds the step itself (a department
        // with no representative, a dean deactivated or not yet assigned), the people who may approve past it are told instead
        // of no one: the college committee or the dean, whose direct approval with a written reason moves the form on.
        if (await HasHolderAsync(step, unit, allowDirect: false, ct)) return regular;
        var direct = await UsersWhereAsync(Need.Review, a => ApprovalRules.MayReview(a, unit, step, allowDirect: true) && UnitScope.For(a, Features.Forms, Cap.View).Covers(unit), ct, everyone);
        return [.. regular.Concat(direct).DistinctBy(r => r.UserId)];
    }

    /// <summary>
    /// G-03: whether an active person other than a super administrator can decide on the form at this step: its own reviewer, or with
    /// <paramref name="allowDirect"/> one who may approve past it. A step nobody holds stops the form until a role is assigned.
    /// </summary>
    internal async Task<bool> HasHolderAsync(ApprovalStep step, OrgUnit unit, bool allowDirect, CancellationToken ct) =>
        step is not (ApprovalStep.Entry or ApprovalStep.Done)
        && (await UsersWhereAsync(Need.Review, a => !a.IsSuperAdmin && ApprovalRules.MayReview(a, unit, step, allowDirect) && UnitScope.For(a, Features.Forms, Cap.View).Covers(unit), ct)).Count > 0;

    /// <summary>من أدخل قيم النموذج، وإلا كل من يملك الإدخال في نطاق الجهة.</summary>
    private async Task<List<Recipient>> EditorsAsync(Guid submissionId, OrgUnit unit, CancellationToken ct)
    {
        var editors = await db.SubmittedValues.AsNoTracking().Where(v => v.SubmissionId == submissionId).Select(v => v.EditedBy).Distinct().ToListAsync(ct);
        var active = editors.Count == 0 ? [] : await read.Query<Domain.Identity.AppUser>().AsNoTracking().Where(u => u.Active && editors.Contains(u.Id)).OrderBy(u => u.Email).Select(u => new Recipient(u.Id, u.Email)).ToListAsync(ct);
        // G-17: when everyone who entered the values has left (deactivated), whoever may enter them now is told instead of no one.
        return active.Count > 0 ? active : await UnitEditorsAsync(unit, ct);
    }

    /// <summary>Everyone who may enter the unit's values (the editors of a form nobody has touched yet).</summary>
    internal async Task<List<Recipient>> UnitEditorsAsync(OrgUnit unit, CancellationToken ct, bool everyone = false) =>
        await UsersWhereAsync(Need.Edit, a => a.Has(Features.Forms, Cap.Edit) && UnitScope.For(a, Features.Forms, Cap.Edit).Covers(unit) && !a.IsSuperAdmin, ct, everyone);

    private enum Need { Review, Edit }

    /// <summary>
    /// PERF-B1: the rule (MayReview, the unit scope) is applied as before, but only to the accounts that could pass it: holders of a
    /// role or an allowing exception that grants a review action (or the edit capability) on forms, and super administrators.
    /// That is tens of accounts instead of every active one, each costing 4-5 queries on a cache miss inside the approval request.
    /// <paramref name="everyone"/> evaluates every active account (the old way), for the equivalence test only.
    /// </summary>
    private async Task<List<Recipient>> UsersWhereAsync(Need need, Func<EffectiveAccess, bool> predicate, CancellationToken ct, bool everyone = false) =>
        [.. (await CandidatesAsync(need, ct, everyone)).Where(c => predicate(c.Access)).Select(c => c.Recipient)];

    /// <summary>
    /// G-03 (system integration review 2026-11-24): for each unit, what nobody but a super administrator holds: entering its data, or
    /// the reviewer of a step of its approval path. A step that only a direct approval can pass is marked so; one that nobody can take
    /// stops the form. Shown before a cycle opens. The people are read once for all the units.
    /// </summary>
    internal async Task<List<(OrgUnit Unit, List<string> Missing)>> CoverageAsync(IReadOnlyList<OrgUnit> units, CancellationToken ct)
    {
        var reviewers = (await CandidatesAsync(Need.Review, ct)).Select(c => c.Access).Where(a => !a.IsSuperAdmin).ToList();
        var editors = (await CandidatesAsync(Need.Edit, ct)).Select(c => c.Access).Where(a => !a.IsSuperAdmin).ToList();
        var result = new List<(OrgUnit, List<string>)>();
        foreach (var unit in units)
        {
            var missing = new List<string>();
            if (!editors.Any(a => a.Has(Features.Forms, Cap.Edit) && UnitScope.For(a, Features.Forms, Cap.Edit).Covers(unit))) missing.Add(ApprovalRules.StepName(ApprovalStep.Entry));
            foreach (var step in ApprovalRules.PathFor(unit.Kind).Skip(1))
            {
                bool Held(bool direct) => reviewers.Any(a => ApprovalRules.MayReview(a, unit, step, direct) && UnitScope.For(a, Features.Forms, Cap.View).Covers(unit));
                // A step whose own reviewer is missing still moves when someone may approve past it, with a written reason.
                if (!Held(direct: false)) missing.Add(Held(direct: true) ? $"{ApprovalRules.StepName(step)} (يمر باعتماد مباشر)" : ApprovalRules.StepName(step));
            }
            if (missing.Count > 0) result.Add((unit, missing));
        }
        return result;
    }

    /// <summary>G-03: the (unit, step) pairs nobody but a super administrator can decide, even by a direct approval. People are read once.</summary>
    internal async Task<HashSet<(Guid UnitId, ApprovalStep Step)>> UnheldAsync(IEnumerable<(OrgUnit Unit, ApprovalStep Step)> pairs, CancellationToken ct)
    {
        var reviewers = (await CandidatesAsync(Need.Review, ct)).Select(c => c.Access).Where(a => !a.IsSuperAdmin).ToList();
        return [.. pairs.Where(p => p.Step is not (ApprovalStep.Entry or ApprovalStep.Done)
                && !reviewers.Any(a => ApprovalRules.MayReview(a, p.Unit, p.Step, allowDirect: true) && UnitScope.For(a, Features.Forms, Cap.View).Covers(p.Unit)))
            .Select(p => (p.Unit.Id, p.Step))];
    }

    private async Task<List<(Recipient Recipient, EffectiveAccess Access)>> CandidatesAsync(Need need, CancellationToken ct, bool everyone = false)
    {
        var forms = Features.Forms;
        var roles = need == Need.Review
            ? read.Query<Domain.Access.Role>().Where(r => r.Code == PermissionCatalog.SuperAdminRole || r.Grants.Any(g => g.Feature == forms && g.Specials.Count > 0))
            : read.Query<Domain.Access.Role>().Where(r => r.Code == PermissionCatalog.SuperAdminRole || r.Grants.Any(g => g.Feature == forms && (g.Caps & Cap.Edit) == Cap.Edit));
        var roleIds = roles.Select(r => r.Id);
        var viaRoles = read.Query<Domain.Access.UserRole>().Where(a => a.RevokedAt == null && roleIds.Contains(a.RoleId)).Select(a => a.UserId);
        var viaOverrides = need == Need.Review
            ? read.Query<Domain.Access.UserOverride>().Where(o => o.RevokedAt == null && o.Effect == OverrideEffect.Allow && o.Feature == forms && o.Specials.Count > 0).Select(o => o.UserId)
            : read.Query<Domain.Access.UserOverride>().Where(o => o.RevokedAt == null && o.Effect == OverrideEffect.Allow && o.Feature == forms && (o.Caps & Cap.Edit) == Cap.Edit).Select(o => o.UserId);
        var candidates = read.Query<Domain.Identity.AppUser>().AsNoTracking().Where(u => u.Active);
        if (!everyone) candidates = candidates.Where(u => viaRoles.Contains(u.Id) || viaOverrides.Contains(u.Id));
        var users = await candidates.OrderBy(u => u.Email).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        var result = new List<(Recipient, EffectiveAccess)>();
        foreach (var u in users) result.Add((new Recipient(u.Id, u.Email), await access.GetAsync(u.Id, ct)));
        return result;
    }
}
