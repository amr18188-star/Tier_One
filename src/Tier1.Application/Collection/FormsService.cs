using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

public sealed record FormRow(
    Guid Id, Guid CycleId, string CycleName, Guid UnitId, string UnitName, OrgUnitKind UnitKind, string? ParentName,
    ApprovalStep Step, string StepName, SubmissionStatus Status, DateOnly DueOn, bool Overdue, int Completed, int Required, bool AwaitingMe);

public sealed record PathStep(ApprovalStep Step, string Name, string State);

public sealed record IssueDto(string Code, IssueSeverity Severity, string MessageAr);

public sealed record DocDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, string? Description, bool Voided, string? VoidReason);

public sealed record ValueDto(
    ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, string? Reason, string? Explanation, string? ReviewNote, DateTimeOffset EditedAt, uint Version);

public sealed record FormItem(
    Guid IndicatorId, string Code, string Name, string Unit, Aggregation Aggregation, bool UsesRatio, string? NumeratorLabel, string? DenominatorLabel,
    string? Definition, string? DataSource, string? EvidenceRequired, Direction Direction, decimal? Previous, decimal? Target,
    ValueDto? Value, IReadOnlyList<IssueDto> Issues, IReadOnlyList<DocDto> Evidence, string? EntryNote, Kpi.TargetSource TargetSource,
    decimal? PendingTarget = null, int? PendingTargetYear = null, ApprovedValueDto? ApprovedBefore = null, Kpi.ShownTarget? ShownTarget = null);

/// <summary>معيار في النموذج بركيزته، فيعرض الإدخال مصنفًا: الركيزة ← المعيار ← مؤشراته.</summary>
public sealed record FormSection(string? PillarCode, string? PillarName, string StandardCode, string StandardName, IReadOnlyList<FormItem> Items);

public sealed record FormEventDto(SubmissionAction Action, ApprovalStep FromStep, ApprovalStep ToStep, string? ByName, DateTimeOffset At, string? Note);

/// <param name="CanReview">يملك قرارًا على النموذج في خطوته (اعتماد عادي أو مباشر، والإعادة للتصحيح).</param>
/// <param name="CanApprove">يملك الاعتماد العادي إلى الخطوة التالية (NextStepName).</param>
/// <param name="DirectStepName">إن ملك الاعتماد المباشر: الخطوة التي يحيل إليها. يحتاج سببًا مكتوبًا (D2).</param>
public sealed record FormActions(bool CanEdit, bool CanSubmit, bool CanReview, bool CanReopen, bool DirectDepartmentApproval, bool DirectDeanApproval,
    bool CanApprove = false, string? DirectStepName = null, bool CanDecideReopen = false);

public sealed record FormDto(
    Guid Id, Guid CycleId, string CycleName, string PeriodLabel, CycleStatus CycleStatus, bool Trial, int TargetYear,
    Guid UnitId, string UnitName, OrgUnitKind UnitKind, ApprovalStep Step, SubmissionStatus Status, DateOnly DueOn, bool Overdue,
    IReadOnlyList<PathStep> Path, string? NextStepName, string? ReturnNote, DateTimeOffset? LastEditedAt,
    int Completed, int Required, IReadOnlyList<FormSection> Sections, IReadOnlyList<string> Blocking,
    IReadOnlyList<FormEventDto> Events, FormActions Actions, uint Version, FormReopenDto? Reopen = null);

public sealed record SaveValueRequest(Guid IndicatorId, ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, string? Reason, string? Explanation, uint? Version);

public sealed record ReturnItem(Guid IndicatorId, string Note);

public sealed record ReturnRequest(string? Note, IReadOnlyList<ReturnItem>? Items, uint? Version = null);

/// <summary>انتقال من إصدار النموذج الذي رآه المستخدم (الإرسال وطلب إعادة الفتح)؛ الملاحظة لطلب إعادة الفتح.</summary>
public sealed record FormTransitionRequest(uint? Version, string? Note = null);

public sealed record NoteRequest(string? Note);

/// <param name="Direct">اعتماد مباشر يتخطى خطوة أو خطوتين؛ يلزمه Note سببًا (5 أحرف على الأقل).</param>
public sealed record ApproveFormRequest(string? Note, uint? Version, bool Direct = false);

public sealed record ExtendRequest(DateOnly DueOn, string? Reason);

/// <summary>
/// نماذج الجهات: الإدخال بالتحقق الفوري، والإرسال بعد اكتمال الإلزامي، والمراجعة والاعتماد على مسار الجهة بفصل المهام،
/// والإعادة بملاحظات تبرز الحقول، والقفل بعد الاعتماد النهائي وإعادة الفتح بسبب. كل ذلك في نطاق المستخدم على الجهات.
/// ARCH-1: this class lists forms and shows one. Entry is <see cref="FormEntryService"/>, the path is <see cref="FormWorkflowService"/>,
/// loading and building a form is <see cref="FormReader"/>, and which indicators a unit enters is <see cref="FormAssignmentRules"/>.
/// </summary>
public sealed class FormsService(ICollectionStore db, IReadModel read, IClock clock, ICurrentUser current, IAccessService access, FormReader reader)
{
    private const string Feature = Features.Forms;

    public async Task<IReadOnlyList<FormRow>> ListAsync(Guid? cycleId, bool awaitingMe, SubmissionStatus? status, string? q, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var view = UnitScope.For(me, Feature, Cap.View);
        var edit = UnitScope.For(me, Feature, Cap.Edit);
        // PERF-B2/BE-17: a closed cycle's forms are listed only when that cycle is asked for. Without a cycle the list is the open
        // cycles' forms, so it does not grow with every past quarter (and a closed cycle's form never awaits anyone).
        var cycles = await db.Cycles.AsNoTracking()
            .Where(c => cycleId == null ? c.Status == CycleStatus.Open : c.Id == cycleId && (c.Status == CycleStatus.Open || c.Status == CycleStatus.Closed))
            .ToDictionaryAsync(c => c.Id, ct);
        var ids = cycles.Keys.ToList();
        // PERF-B2/BE-17: scope and search are decided on the unit tree (a few hundred rows, needed for the names anyway) and reach
        // SQL as unit ids with the status, so only the forms the user may see and asked for are read. The list stays whole per cycle:
        // it is bounded by the number of units, and the page groups colleges with their departments and counts over all of them.
        var units = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var term = q?.Trim();
        var forms = db.Submissions.AsNoTracking().Where(s => ids.Contains(s.CycleId));
        if (!view.All || !string.IsNullOrEmpty(term))
        {
            var unitIds = units.Values
                .Where(u => view.Covers(u) && (string.IsNullOrEmpty(term) || u.NameAr.Contains(term, StringComparison.Ordinal)
                                               || (u.ParentId is { } parent && units[parent].NameAr.Contains(term, StringComparison.Ordinal))))
                .Select(u => u.Id).ToList();
            forms = forms.Where(s => unitIds.Contains(s.UnitId));
        }
        if (status is { } wanted) forms = forms.Where(s => s.Status == wanted);
        var subs = await forms.ToListAsync(ct);
        var subIds = subs.Select(s => s.Id).ToList();
        var userId = current.UserId!.Value;
        var editedByMe = (await db.SubmittedValues.AsNoTracking().Where(v => subIds.Contains(v.SubmissionId) && v.EditedBy == userId)
            .Select(v => v.SubmissionId).Distinct().ToListAsync(ct)).ToHashSet();
        // ARCH-6/CR-4: the user's own approvals, judged by the same round rule as the form page and the server check.
        var approvedByMe = (await db.SubmissionEvents.AsNoTracking().Where(e => subIds.Contains(e.SubmissionId) && e.By == userId && e.Action == SubmissionAction.Approved)
            .ToListAsync(ct)).ToLookup(e => e.SubmissionId);
        // D4: طلبات إعادة الفتح المعلقة تنتظر من يقررها (اللجنة العليا)، ما لم يكن مقدمها.
        var reopenPending = me.HasSpecial(Features.Cycles, ApprovalRules.ApproveReopen)
            ? (await db.ReopenRequests.AsNoTracking().Where(r => subIds.Contains(r.SubmissionId) && r.Status == ReopenRequestStatus.Pending && r.RequestedBy != current.UserId)
                .Select(r => r.SubmissionId).ToListAsync(ct)).ToHashSet()
            : [];
        var today = CairoTime.DateOf(clock.UtcNow);

        bool Waiting(Submission s) => cycles[s.CycleId].Status == CycleStatus.Open && (
            (s.IsEditable && edit.Covers(units[s.UnitId]) && me.Has(Feature, Cap.Edit))
            || ApprovalRules.CanDecide(me, units[s.UnitId], s, userId, editedByMe.Contains(s.Id) ? [userId] : [], approvedByMe[s.Id])
            || reopenPending.Contains(s.Id));
        var listed = subs.Select(s => (Form: s, Waiting: Waiting(s))).Where(x => !awaitingMe || x.Waiting).ToList();

        // Entered and required counts only for the forms returned (the home page asks for the few awaiting the user).
        var listedIds = listed.Select(x => x.Form.Id).ToList();
        Dictionary<Guid, int> counts = listedIds.Count == 0 ? [] : await db.SubmittedValues.AsNoTracking().Where(v => listedIds.Contains(v.SubmissionId)).GroupBy(v => v.SubmissionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var listedCycles = listed.Select(x => x.Form.CycleId).Distinct().ToList();
        var cis = listedCycles.Count == 0 ? Enumerable.Empty<CycleIndicator>().ToLookup(i => i.CycleId)
            : (await db.CycleIndicators.AsNoTracking().Where(i => listedCycles.Contains(i.CycleId)).ToListAsync(ct)).ToLookup(i => i.CycleId);

        return listed
            .Select(x =>
            {
                var s = x.Form;
                var unit = units[s.UnitId];
                var cycle = cycles[s.CycleId];
                return new FormRow(s.Id, s.CycleId, cycle.Name, unit.Id, unit.NameAr, unit.Kind, unit.ParentId is { } p ? units[p].NameAr : null,
                    s.Step, ApprovalRules.StepName(s.Step), s.Status, s.DueOn, s.Status != SubmissionStatus.Approved && s.DueOn < today && cycle.Status == CycleStatus.Open,
                    counts.GetValueOrDefault(s.Id), cis[s.CycleId].Count(ci => FormAssignmentRules.Applies(ci, unit)), x.Waiting);
            })
            .OrderByDescending(r => r.AwaitingMe).ThenBy(r => r.Status == SubmissionStatus.Approved).ThenBy(r => r.DueOn).ThenBy(r => r.ParentName).ThenBy(r => r.UnitName)
            .ToList();
    }

    public async Task<FormDto> GetAsync(Guid id, CancellationToken ct)
    {
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: false, ct);
        return await reader.BuildAsync(ctx, ct);
    }

}
