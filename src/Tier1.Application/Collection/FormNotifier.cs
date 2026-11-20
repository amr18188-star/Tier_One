using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
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
public sealed class FormNotifier(IAppDb db, IAccessService access, IMailOutbox mail, IConfiguration config)
{
    private string BaseUrl => (config["App:PublicUrl"] is { Length: > 0 } u ? u : "http://localhost:5150").TrimEnd('/');

    /// <summary>
    /// Runs in the form.moved handler, after the transition committed. The message describes the form as the event saw it (its step
    /// and note then); the recipients are whoever holds the step's access now.
    /// </summary>
    public async Task MovedAsync(FormMoved s, CancellationToken ct)
    {
        var unit = await db.OrgUnits.AsNoTracking().FirstAsync(u => u.Id == s.UnitId, ct);
        var cycle = await db.Cycles.AsNoTracking().FirstAsync(c => c.Id == s.CycleId, ct);
        var link = $"{BaseUrl}/forms/{s.SubmissionId}";
        var (recipients, subject, body) = s.Status switch
        {
            SubmissionStatus.InReview => (await ReviewersAsync(s.Step, unit, ct),
                $"نموذج {unit.NameAr} ينتظر مراجعتك — {cycle.Name}",
                $"أرسل نموذج {unit.NameAr} في دورة «{cycle.Name}» وهو الآن عند خطوة «{ApprovalRules.StepName(s.Step)}».\nالموعد النهائي: {s.DueOn:yyyy-MM-dd}\nافتح النموذج: {link}"),
            SubmissionStatus.Returned or SubmissionStatus.InProgress => (await EditorsAsync(s.SubmissionId, unit, ct),
                $"أعيد نموذج {unit.NameAr} للتصحيح — {cycle.Name}",
                $"أعيد نموذج {unit.NameAr} في دورة «{cycle.Name}» بالملاحظة:\n{s.ReturnNote}\nالمؤشرات المطلوب تصحيحها مميزة في النموذج: {link}"),
            SubmissionStatus.Approved => (await EditorsAsync(s.SubmissionId, unit, ct),
                $"اعتمد نموذج {unit.NameAr} نهائيًا — {cycle.Name}",
                $"اعتمدت الجودة المركزية نموذج {unit.NameAr} في دورة «{cycle.Name}». شكرًا لكم.\n{link}"),
            _ => ([], "", ""),
        };
        // Adds the mails only; the event dispatcher saves them together with the record that this handler ran.
        foreach (var email in recipients) mail.Enqueue("dc.form", email, subject, body);
    }

    /// <summary>تذكير يومي بالنماذج المتأخرة أو القريبة من موعدها: لمن عليه الإجراء الآن، مرة في اليوم لكل نموذج.</summary>
    public async Task<int> RemindAsync(DateOnly today, int daysBefore, CancellationToken ct)
    {
        var open = await db.Cycles.AsNoTracking().Where(c => c.Status == CycleStatus.Open).ToDictionaryAsync(c => c.Id, ct);
        var ids = open.Keys.ToList();
        var limit = today.AddDays(daysBefore);
        var forms = await db.Submissions.AsNoTracking().Where(s => ids.Contains(s.CycleId) && s.Status != SubmissionStatus.Approved && s.DueOn <= limit).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var sent = 0;
        foreach (var s in forms)
        {
            var key = $"rem:{s.Id:N}:{today:yyyyMMdd}";
            if (await db.OutgoingMails.AnyAsync(m => m.DedupKey != null && m.DedupKey.StartsWith(key), ct)) continue;
            var unit = units[s.UnitId];
            var to = s.Status == SubmissionStatus.InReview ? await ReviewersAsync(s.Step, unit, ct) : await EditorsAsync(s.Id, unit, ct);
            var late = s.DueOn < today;
            foreach (var email in to)
            {
                mail.Enqueue("dc.reminder", email,
                    late ? $"نموذج {unit.NameAr} متأخر عن موعده — {open[s.CycleId].Name}" : $"تذكير: موعد نموذج {unit.NameAr} يقترب — {open[s.CycleId].Name}",
                    $"نموذج {unit.NameAr} عند «{ApprovalRules.StepName(s.Step)}»، والموعد النهائي {s.DueOn:yyyy-MM-dd}{(late ? " وقد مضى" : "")}.\n{BaseUrl}/forms/{s.Id}", $"{key}:{sent}");
                sent++;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    internal async Task<List<string>> ReviewersAsync(ApprovalStep step, OrgUnit unit, CancellationToken ct, bool everyone = false)
    {
        if (step is ApprovalStep.Entry or ApprovalStep.Done) return [];
        return await UsersWhereAsync(Need.Review, a => ApprovalRules.MayReview(a, unit, step, allowDirect: false) && UnitScope.For(a, Features.Forms, Cap.View).Covers(unit), ct, everyone);
    }

    /// <summary>من أدخل قيم النموذج، وإلا كل من يملك الإدخال في نطاق الجهة.</summary>
    private async Task<List<string>> EditorsAsync(Guid submissionId, OrgUnit unit, CancellationToken ct)
    {
        var editors = await db.SubmittedValues.AsNoTracking().Where(v => v.SubmissionId == submissionId).Select(v => v.EditedBy).Distinct().ToListAsync(ct);
        if (editors.Count > 0)
            return await db.Users.AsNoTracking().Where(u => u.Active && editors.Contains(u.Id)).Select(u => u.Email).ToListAsync(ct);
        return await UnitEditorsAsync(unit, ct);
    }

    /// <summary>Everyone who may enter the unit's values (the editors of a form nobody has touched yet).</summary>
    internal async Task<List<string>> UnitEditorsAsync(OrgUnit unit, CancellationToken ct, bool everyone = false) =>
        await UsersWhereAsync(Need.Edit, a => a.Has(Features.Forms, Cap.Edit) && UnitScope.For(a, Features.Forms, Cap.Edit).Covers(unit) && !a.IsSuperAdmin, ct, everyone);

    private enum Need { Review, Edit }

    /// <summary>
    /// PERF-B1: the rule (MayReview, the unit scope) is applied as before, but only to the accounts that could pass it: holders of a
    /// role or an allowing exception that grants a review action (or the edit capability) on forms, and super administrators.
    /// That is tens of accounts instead of every active one, each costing 4-5 queries on a cache miss inside the approval request.
    /// <paramref name="everyone"/> evaluates every active account (the old way), for the equivalence test only.
    /// </summary>
    private async Task<List<string>> UsersWhereAsync(Need need, Func<EffectiveAccess, bool> predicate, CancellationToken ct, bool everyone = false)
    {
        var forms = Features.Forms;
        var roles = need == Need.Review
            ? db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole || r.Grants.Any(g => g.Feature == forms && g.Specials.Count > 0))
            : db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole || r.Grants.Any(g => g.Feature == forms && (g.Caps & Cap.Edit) == Cap.Edit));
        var roleIds = roles.Select(r => r.Id);
        var viaRoles = db.UserRoles.Where(a => a.RevokedAt == null && roleIds.Contains(a.RoleId)).Select(a => a.UserId);
        var viaOverrides = need == Need.Review
            ? db.UserOverrides.Where(o => o.RevokedAt == null && o.Effect == OverrideEffect.Allow && o.Feature == forms && o.Specials.Count > 0).Select(o => o.UserId)
            : db.UserOverrides.Where(o => o.RevokedAt == null && o.Effect == OverrideEffect.Allow && o.Feature == forms && (o.Caps & Cap.Edit) == Cap.Edit).Select(o => o.UserId);
        var candidates = db.Users.AsNoTracking().Where(u => u.Active);
        if (!everyone) candidates = candidates.Where(u => viaRoles.Contains(u.Id) || viaOverrides.Contains(u.Id));
        var users = await candidates.OrderBy(u => u.Email).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        var result = new List<string>();
        foreach (var u in users)
            if (predicate(await access.GetAsync(u.Id, ct))) result.Add(u.Email);
        return result;
    }
}
