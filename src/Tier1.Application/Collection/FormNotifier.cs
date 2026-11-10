using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

/// <summary>
/// إشعارات مسار النماذج بالبريد: من عليه الإجراء التالي يصله رابط النموذج مباشرة. المستلمون يحسبون من الصلاحيات الفعلية:
/// مراجع الخطوة (إجراؤها الخاص ونطاقه يغطي الجهة)، أو مدخلو البيانات عند الإعادة. الرسالة تضاف في معاملة العملية نفسها.
/// </summary>
public sealed class FormNotifier(IAppDb db, IAccessService access, IMailOutbox mail, IConfiguration config)
{
    private string BaseUrl => (config["App:PublicUrl"] is { Length: > 0 } u ? u : "http://localhost:5150").TrimEnd('/');

    public async Task MovedAsync(Submission s, OrgUnit unit, CollectionCycle cycle, CancellationToken ct)
    {
        var link = $"{BaseUrl}/forms/{s.Id}";
        var (recipients, subject, body) = s.Status switch
        {
            SubmissionStatus.InReview => (await ReviewersAsync(s.Step, unit, ct),
                $"نموذج {unit.NameAr} ينتظر مراجعتك — {cycle.Name}",
                $"أرسل نموذج {unit.NameAr} في دورة «{cycle.Name}» وهو الآن عند خطوة «{ApprovalRules.StepName(s.Step)}».\nالموعد النهائي: {s.DueOn:yyyy-MM-dd}\nافتح النموذج: {link}"),
            SubmissionStatus.Returned or SubmissionStatus.InProgress => (await EditorsAsync(s, unit, ct),
                $"أعيد نموذج {unit.NameAr} للتصحيح — {cycle.Name}",
                $"أعيد نموذج {unit.NameAr} في دورة «{cycle.Name}» بالملاحظة:\n{s.ReturnNote}\nالمؤشرات المطلوب تصحيحها مميزة في النموذج: {link}"),
            SubmissionStatus.Approved => (await EditorsAsync(s, unit, ct),
                $"اعتمد نموذج {unit.NameAr} نهائيًا — {cycle.Name}",
                $"اعتمدت الجودة المركزية نموذج {unit.NameAr} في دورة «{cycle.Name}». شكرًا لكم.\n{link}"),
            _ => ([], "", ""),
        };
        foreach (var email in recipients) mail.Enqueue("dc.form", email, subject, body);
        if (recipients.Count > 0) await db.SaveChangesAsync(ct);
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
            var to = s.Status == SubmissionStatus.InReview ? await ReviewersAsync(s.Step, unit, ct) : await EditorsAsync(s, unit, ct);
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

    private async Task<List<string>> ReviewersAsync(ApprovalStep step, OrgUnit unit, CancellationToken ct)
    {
        if (step is ApprovalStep.Entry or ApprovalStep.Done) return [];
        var special = ApprovalRules.SpecialFor(step);
        return await UsersWhereAsync(a => a.HasSpecial(Features.Forms, special) && UnitScope.For(a, Features.Forms, Cap.View).Covers(unit), ct);
    }

    /// <summary>من أدخل قيم النموذج، وإلا كل من يملك الإدخال في نطاق الجهة.</summary>
    private async Task<List<string>> EditorsAsync(Submission s, OrgUnit unit, CancellationToken ct)
    {
        var editors = await db.SubmittedValues.AsNoTracking().Where(v => v.SubmissionId == s.Id).Select(v => v.EditedBy).Distinct().ToListAsync(ct);
        if (editors.Count > 0)
            return await db.Users.AsNoTracking().Where(u => u.Active && editors.Contains(u.Id)).Select(u => u.Email).ToListAsync(ct);
        return await UsersWhereAsync(a => a.Has(Features.Forms, Cap.Edit) && UnitScope.For(a, Features.Forms, Cap.Edit).Covers(unit) && !a.IsSuperAdmin, ct);
    }

    private async Task<List<string>> UsersWhereAsync(Func<EffectiveAccess, bool> predicate, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.Active).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        var result = new List<string>();
        foreach (var u in users)
            if (predicate(await access.GetAsync(u.Id, ct))) result.Add(u.Email);
        return result;
    }
}
