using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Kpi;
using Tier1.Application.Structure;
using Tier1.Domain.Access;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Home;

/// <summary>مهمة تنتظر المستخدم؛ الرابط يفتح مكان تنفيذها مباشرة بفلترها.</summary>
public sealed record TaskItem(string Key, string TitleAr, string HintAr, int Count, string Link, string Tone);

public sealed record CountItem(string Key, string LabelAr, int Count);

public sealed record StandardProgress(string Code, string NameAr, int Indicators, int Approved, int OpenConflicts);

public sealed record HomeDto(
    string DisplayName, IReadOnlyList<TaskItem> Tasks, IReadOnlyList<CountItem> DefinitionStatus, IReadOnlyList<CountItem> ConflictSeverity,
    int OpenConflicts, int ResolvedConflicts, IReadOnlyList<StandardProgress> Standards, int? StructureAveragePercent, int CollegeCount);

/// <summary>
/// الصفحة الأولى: «مطلوب منك» بحسب صلاحيات المستخدم ونطاقه، ثم الصورة العامة للقاموس والتعارضات والهيكل.
/// كل مهمة تحمل رابطًا مباشرًا، فيصل المستخدم إلى ما يريده بنقرة.
/// </summary>
public sealed class HomeService(IReadModel read, ICurrentUser current, IAccessService access, DictionaryService dictionary, StructureService structure, Collection.FormsService forms,
    Scorecard.ReviewService review)
{
    public async Task<HomeDto> GetAsync(CancellationToken ct)
    {
        var me = await access.CurrentAsync(ct);
        var tasks = new List<TaskItem>();

        // النماذج أولًا: هي العمل اليومي في الدورة المفتوحة.
        if (me.Has(Features.Forms, Cap.View))
        {
            var mine = await forms.ListAsync(null, true, null, null, ct);
            var returned = mine.Count(f => f.Status == Domain.Collection.SubmissionStatus.Returned);
            if (returned > 0)
                tasks.Add(new("forms_returned", "نماذج أعيدت للتصحيح", "صحح المؤشرات المميزة بملاحظات المراجع ثم أعد الإرسال.", returned, "/forms?view=Returned", "bad"));
            var fill = mine.Count(f => f.Status is Domain.Collection.SubmissionStatus.NotStarted or Domain.Collection.SubmissionStatus.InProgress);
            if (fill > 0)
                tasks.Add(new("forms_fill", "نماذج تنتظر إدخال بياناتك", mine.Any(f => f.Overdue) ? "بعضها تجاوز موعده النهائي." : "أدخل القيم وأرفق الأدلة ثم أرسل.", fill, "/forms", mine.Any(f => f.Overdue) ? "bad" : "brand"));
            var review = mine.Count(f => f.Status == Domain.Collection.SubmissionStatus.InReview);
            if (review > 0)
                tasks.Add(new("forms_review", "نماذج تنتظر مراجعتك", "راجع القيم والأدلة، ثم اعتمد أو أعد بملاحظات.", review, "/forms", "brand"));
        }

        // The definitions being prepared, read only for who has a task in them (editors and approvers of the dictionary).
        var versions = me.Has(Features.Dictionary, Cap.Edit) || me.HasSpecial(Features.Dictionary, Specials.Approve)
            ? await read.Query<Domain.Kpi.IndicatorVersion>().AsNoTracking().Where(v => v.Status == DefinitionStatus.Draft || v.Status == DefinitionStatus.UnderReview).ToListAsync(ct)
            : [];
        if (me.Has(Features.Dictionary, Cap.Edit))
        {
            var returned = versions.Count(v => v.Status == DefinitionStatus.Draft && v.ReturnNote != null);
            if (returned > 0)
                tasks.Add(new("definitions_returned", "تعريفات أعيدت بملاحظات", "راجع ملاحظات اللجنة ثم أعد الإرسال.", returned, "/indicators?status=Draft", "bad"));
            var ready = versions.Count(v => v.Status == DefinitionStatus.Draft && IndicatorRules.MissingForApproval(v).Count == 0);
            if (ready > 0)
                tasks.Add(new("definitions_ready", "تعريفات مكتملة جاهزة للإرسال", "اكتملت بياناتها الإلزامية؛ أرسلها للاعتماد.", ready, "/indicators?status=Draft", "brand"));
            var incomplete = versions.Count(v => v.Status == DefinitionStatus.Draft && IndicatorRules.MissingForApproval(v).Count > 0);
            if (incomplete > 0)
                tasks.Add(new("definitions_incomplete", "تعريفات تنقصها بيانات", "أكمل التعريف والوحدة والمصدر والجهة المالكة وقاعدة التجميع.", incomplete, "/indicators?status=Draft", "warn"));
        }
        if (me.HasSpecial(Features.Dictionary, Specials.Approve))
        {
            var review = versions.Count(v => v.Status == DefinitionStatus.UnderReview && v.SubmittedBy != current.UserId && v.EditedBy != current.UserId);
            if (review > 0)
                tasks.Add(new("definitions_review", "تعريفات تنتظر اعتمادك", "اعتمدها برقم المحضر، أو أعدها بملاحظات.", review, "/indicators?status=UnderReview", "brand"));
        }
        if (me.HasSpecial(Features.Cycles, Collection.ApprovalRules.ApproveReopen))
        {
            var reopen = await read.Query<Domain.Collection.ReopenRequest>().AsNoTracking().Where(r => r.Status == Domain.Collection.ReopenRequestStatus.Pending && r.RequestedBy != current.UserId)
                .Select(r => r.SubmissionId).ToListAsync(ct);
            if (reopen.Count > 0)
                tasks.Add(new("reopen_requests", "طلبات إعادة فتح نماذج معتمدة", "اعتمدها برقم المحضر، أو ارفضها بسبب.", reopen.Count,
                    reopen.Count == 1 ? $"/forms/{reopen[0]}" : "/forms", "warn"));
        }
        if (me.HasSpecial(Features.Targets, Specials.Approve))
        {
            // G-04: "each college" targets are proposals too; they were left out of the count.
            var proposed = await read.Query<Domain.Kpi.IndicatorTarget>().CountAsync(t => t.Status == TargetStatus.Proposed && t.UpdatedBy != current.UserId, ct);
            if (proposed > 0)
                tasks.Add(new("targets_proposed", "مستهدفات مقترحة تنتظر الاعتماد", "اعتمدها من صفحة كل مؤشر برقم المحضر.", proposed, "/indicators", "info"));
        }
        // G-04, G-05: values approved by the units wait for the university Tier 1 committee, and changed ones keep the scorecard old.
        if (me.Has(Features.University, Cap.Edit))
        {
            var (ready, changed, cycleId) = await review.PendingTransfersAsync(ct);
            if (changed > 0)
                tasks.Add(new("transfers_changed", "قيم تغيّرت بعد نقلها إلى مؤشرات الجامعة", "بطاقة الأداء تعرض القيم القديمة حتى تنقلها من جديد.", changed, $"/review?cycle={cycleId}", "bad"));
            if (ready > 0)
                tasks.Add(new("transfers_ready", "مؤشرات جاهزة للنقل إلى مؤشرات الجامعة", "راجع قيم الكليات ثم انقلها.", ready, $"/review?cycle={cycleId}", "brand"));
        }
        // G-03: forms that nobody but a super administrator can move; they wait until a role is assigned in their unit.
        if (me.Has(Features.Cycles, Cap.Edit))
        {
            var unheld = await forms.UnheldAsync(ct);
            if (unheld.Count > 0)
                tasks.Add(new("forms_unheld", "نماذج عند خطوة لا يملكها أحد", $"أسند دور الخطوة في الجهة، مثل: {unheld[0].Unit} عند «{Collection.ApprovalRules.StepName(unheld[0].Step)}».",
                    unheld.Count, unheld.Count == 1 ? $"/forms/{unheld[0].FormId}" : "/forms", "bad"));
        }
        // G-04: what waits for a second, independent super administrator.
        if (me.IsSuperAdmin)
        {
            var assignments = await read.Query<Domain.Access.UserRole>().CountAsync(a => a.ApprovedAt == null && a.RevokedAt == null && a.GrantedBy != current.UserId && a.UserId != current.UserId, ct);
            if (assignments > 0)
                tasks.Add(new("assignments_pending", "إسناد دور المدير الأعلى ينتظر اعتمادك", "اعتمده من صفحة الحساب إن كان الطلب في محله.", assignments, "/users", "warn"));
            var changes = await read.Query<Domain.Access.RbacChange>().CountAsync(c => c.Status == Domain.Access.RbacChangeStatus.Pending && c.RequestedBy != current.UserId, ct);
            if (changes > 0)
                tasks.Add(new("rbac_changes_pending", "طلبات تعديل صلاحيات تنتظر اعتمادك", "اعتمدها أو ارفضها بسبب.", changes, "/changes", "warn"));
        }

        var conflicts = await read.Query<Domain.Kpi.DataConflict>().AsNoTracking().Select(c => new { c.Status, c.Severity }).ToListAsync(ct);
        // G-01: the conflicts register was retired once the framework was approved (docs/kpi-framework.md); its home tasks led to
        // pages that no longer exist, so none is offered. The counts below still describe the register for those who see it.

        int? structurePercent = null;
        var collegeCount = 0;
        if (me.Has(Features.Structure, Cap.View))
        {
            var colleges = await structure.CompletenessAsync(ct);
            collegeCount = colleges.Count;
            if (colleges.Count > 0) structurePercent = (int)Math.Round(colleges.Average(c => c.Percent));
            if (me.Has(Features.Structure, Cap.Edit))
            {
                var incomplete = colleges.Count(c => c.Percent < 100);
                if (incomplete > 0)
                    tasks.Add(new("structure_incomplete", "هياكل كليات غير مكتملة", "مناصب شاغرة أو أقسام بلا ممثل Tier 1 أو شريك صناعي.", incomplete, "/structure", "warn"));
            }
        }

        var all = await read.Query<Domain.Kpi.IndicatorVersion>().AsNoTracking().Select(v => new { v.IndicatorId, v.Status }).ToListAsync(ct);
        var byIndicator = all.GroupBy(v => v.IndicatorId).Select(g => g.Any(v => v.Status == DefinitionStatus.Approved) && !g.Any(v => v.Status is DefinitionStatus.Draft or DefinitionStatus.UnderReview)
            ? DefinitionStatus.Approved
            : g.Any(v => v.Status == DefinitionStatus.UnderReview) ? DefinitionStatus.UnderReview : DefinitionStatus.Draft).ToList();

        var standards = me.Has(Features.Dictionary, Cap.View) ? await dictionary.StandardsAsync(ct) : [];

        // الأرقام العامة لمن يملك صلاحية القاموس أو التعارضات فقط؛ الصفحة الرئيسية لكل الأدوار، ومن لا يرى محتوى المعاملات (مسؤول النظام) لا تصله أرقامه.
        var seesDictionary = me.Has(Features.Dictionary, Cap.View) || me.Has(Features.Dictionary, Cap.Edit) || me.HasSpecial(Features.Dictionary, Specials.Approve);
        var seesConflicts = me.Has(Features.Conflicts, Cap.View) || me.Has(Features.Conflicts, Cap.Edit) || me.HasSpecial(Features.Conflicts, Specials.Decide);

        return new HomeDto(
            current.DisplayName ?? "",
            tasks,
            !seesDictionary ? [] :
            [
                new("Draft", "مسودة", byIndicator.Count(s => s == DefinitionStatus.Draft)),
                new("UnderReview", "قيد الاعتماد", byIndicator.Count(s => s == DefinitionStatus.UnderReview)),
                new("Approved", "معتمد", byIndicator.Count(s => s == DefinitionStatus.Approved)),
            ],
            !seesConflicts ? [] :
            [
                new("High", "عالية", conflicts.Count(c => c.Severity == ConflictSeverity.High && c.Status != ConflictStatus.Resolved)),
                new("Medium", "متوسطة", conflicts.Count(c => c.Severity == ConflictSeverity.Medium && c.Status != ConflictStatus.Resolved)),
                new("Low", "منخفضة", conflicts.Count(c => c.Severity == ConflictSeverity.Low && c.Status != ConflictStatus.Resolved)),
            ],
            seesConflicts ? conflicts.Count(c => c.Status != ConflictStatus.Resolved) : 0,
            seesConflicts ? conflicts.Count(c => c.Status == ConflictStatus.Resolved) : 0,
            standards.Select(s => new StandardProgress(s.Code, s.NameAr, s.IndicatorCount, s.ApprovedCount, s.OpenConflicts)).ToList(),
            structurePercent,
            collegeCount);
    }
}
