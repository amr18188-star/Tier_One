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
    ValueDto? Value, IReadOnlyList<IssueDto> Issues, IReadOnlyList<DocDto> Evidence);

public sealed record FormSection(string StandardCode, string StandardName, IReadOnlyList<FormItem> Items);

public sealed record FormEventDto(SubmissionAction Action, ApprovalStep FromStep, ApprovalStep ToStep, string? ByName, DateTimeOffset At, string? Note);

public sealed record FormActions(bool CanEdit, bool CanSubmit, bool CanReview, bool CanReopen);

public sealed record FormDto(
    Guid Id, Guid CycleId, string CycleName, string PeriodLabel, CycleStatus CycleStatus, bool Trial, int TargetYear,
    Guid UnitId, string UnitName, OrgUnitKind UnitKind, ApprovalStep Step, SubmissionStatus Status, DateOnly DueOn, bool Overdue,
    IReadOnlyList<PathStep> Path, string? NextStepName, string? ReturnNote, DateTimeOffset? LastEditedAt,
    int Completed, int Required, IReadOnlyList<FormSection> Sections, IReadOnlyList<string> Blocking,
    IReadOnlyList<FormEventDto> Events, FormActions Actions, uint Version);

public sealed record SaveValueRequest(Guid IndicatorId, ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, string? Reason, string? Explanation, uint? Version);

public sealed record ReturnItem(Guid IndicatorId, string Note);

public sealed record ReturnRequest(string? Note, IReadOnlyList<ReturnItem>? Items);

public sealed record NoteRequest(string? Note);

public sealed record ExtendRequest(DateOnly DueOn, string? Reason);

/// <summary>
/// نماذج الجهات: الإدخال بالتحقق الفوري، والإرسال بعد اكتمال الإلزامي، والمراجعة والاعتماد على مسار الجهة بفصل المهام،
/// والإعادة بملاحظات تبرز الحقول، والقفل بعد الاعتماد النهائي وإعادة الفتح بسبب. كل ذلك في نطاق المستخدم على الجهات.
/// </summary>
public sealed partial class FormsService(IAppDb db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, Settings.SettingsService settings, FormNotifier notifier)
{
    private const string Feature = Features.Forms;

    public async Task<IReadOnlyList<FormRow>> ListAsync(Guid? cycleId, bool awaitingMe, SubmissionStatus? status, string? q, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var view = UnitScope.For(me, Feature, Cap.View);
        var edit = UnitScope.For(me, Feature, Cap.Edit);
        var cycles = await db.Cycles.AsNoTracking().Where(c => c.Status == CycleStatus.Open || c.Status == CycleStatus.Closed)
            .Where(c => cycleId == null || c.Id == cycleId).ToDictionaryAsync(c => c.Id, ct);
        var ids = cycles.Keys.ToList();
        var subs = await db.Submissions.AsNoTracking().Where(s => ids.Contains(s.CycleId)).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var cis = await db.CycleIndicators.AsNoTracking().Where(i => ids.Contains(i.CycleId)).ToListAsync(ct);
        var subIds = subs.Select(s => s.Id).ToList();
        var counts = await db.SubmittedValues.AsNoTracking().Where(v => subIds.Contains(v.SubmissionId)).GroupBy(v => v.SubmissionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var editors = await db.SubmittedValues.AsNoTracking().Where(v => subIds.Contains(v.SubmissionId) && v.EditedBy == current.UserId)
            .Select(v => v.SubmissionId).Distinct().ToListAsync(ct);
        var approvedByMe = await db.SubmissionEvents.AsNoTracking().Where(e => subIds.Contains(e.SubmissionId) && e.By == current.UserId && e.Action == SubmissionAction.Approved)
            .Select(e => e.SubmissionId).Distinct().ToListAsync(ct);
        var today = CairoTime.DateOf(clock.UtcNow);
        var term = q?.Trim();

        return subs
            .Where(s => view.Covers(units[s.UnitId]))
            .Select(s =>
            {
                var unit = units[s.UnitId];
                var cycle = cycles[s.CycleId];
                var required = cis.Count(ci => ci.CycleId == s.CycleId && Applies(ci, unit));
                var waiting = cycle.Status == CycleStatus.Open && (
                    (s.IsEditable && edit.Covers(unit) && me.Has(Feature, Cap.Edit))
                    || (s.Status == SubmissionStatus.InReview && me.HasSpecial(Feature, ApprovalRules.SpecialFor(s.Step))
                        && !editors.Contains(s.Id) && !approvedByMe.Contains(s.Id)));
                return new FormRow(s.Id, s.CycleId, cycle.Name, unit.Id, unit.NameAr, unit.Kind, unit.ParentId is { } p ? units[p].NameAr : null,
                    s.Step, ApprovalRules.StepName(s.Step), s.Status, s.DueOn, s.Status != SubmissionStatus.Approved && s.DueOn < today && cycle.Status == CycleStatus.Open,
                    counts.GetValueOrDefault(s.Id), required, waiting);
            })
            .Where(r => !awaitingMe || r.AwaitingMe)
            .Where(r => status is null || r.Status == status)
            .Where(r => string.IsNullOrEmpty(term) || r.UnitName.Contains(term, StringComparison.Ordinal) || (r.ParentName ?? "").Contains(term, StringComparison.Ordinal))
            .OrderByDescending(r => r.AwaitingMe).ThenBy(r => r.Status == SubmissionStatus.Approved).ThenBy(r => r.DueOn).ThenBy(r => r.ParentName).ThenBy(r => r.UnitName)
            .ToList();
    }

    public async Task<FormDto> GetAsync(Guid id, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.View, tracking: false, ct);
        return await BuildAsync(ctx, ct);
    }

    public async Task<FormItem> SaveValueAsync(Guid id, SaveValueRequest req, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.Edit, tracking: true, ct);
        EnsureEditable(ctx);
        var ci = ctx.Indicators.FirstOrDefault(i => i.Ci.IndicatorId == req.IndicatorId)
                 ?? throw new DomainException("not_in_form", "هذا المؤشر ليس مطلوبًا من هذه الجهة.", "This indicator is not required from this unit.", 400);
        var spec = CycleSpec.Of(ci.Version);
        var bigChange = await settings.IntAsync(SettingKeys.BigChangePercent, ct) / 100m;
        var (value, issues) = ValueRules.Check(spec, new ValueInput(req.Kind, req.Value, req.Numerator, req.Denominator, req.Reason, req.Explanation),
            ctx.Previous.GetValueOrDefault(req.IndicatorId), bigChange);
        if (issues.FirstOrDefault(i => i.Severity == IssueSeverity.Error) is { } error)
            throw new DomainException(error.Code, error.MessageAr, error.MessageAr, 400);

        var row = Upsert(ctx, ci, spec, req, value, "dc.value_saved");
        await db.SaveChangesAsync(ct);
        ctx.Values[req.IndicatorId] = row;
        return await ItemAsync(ctx, ci, bigChange, ct);
    }

    /// <summary>كتابة قيمة فحصت مسبقًا في سطرها (جديد أو تعديل بإصدار)، مع سجل التدقيق بالتغييرات؛ الحفظ على المستدعي.</summary>
    private SubmittedValue Upsert(FormContext ctx, IndicatorInCycle ci, ValueSpec spec, SaveValueRequest req, decimal? value, string verb)
    {
        var now = clock.UtcNow;
        var row = ctx.Values.GetValueOrDefault(req.IndicatorId);
        var created = row is null;
        if (row is null)
        {
            row = new SubmittedValue { SubmissionId = ctx.Submission.Id, IndicatorId = req.IndicatorId };
            db.SubmittedValues.Add(row);
        }
        else
        {
            Concurrency.Expect(db, row, req.Version);
        }
        var changes = new ChangeSet().Add("kind", created ? null : row.Kind.ToString(), req.Kind.ToString()).Add("value", row.Value, value)
            .Add("numerator", row.Numerator, req.Numerator).Add("denominator", row.Denominator, req.Denominator);
        row.Kind = req.Kind;
        row.Value = req.Kind == ValueKind.Number ? value : null;
        row.Numerator = req.Kind == ValueKind.Number && ValueRules.UsesRatio(spec) ? req.Numerator : null;
        row.Denominator = req.Kind == ValueKind.Number && (ValueRules.UsesRatio(spec) || spec.Aggregation == Aggregation.WeightedAverage) ? req.Denominator : null;
        row.Reason = req.Kind == ValueKind.Number ? null : Require.Optional(req.Reason, 500);
        row.Explanation = Require.Optional(req.Explanation, 1000);
        row.ReviewNote = null;
        row.EditedBy = current.UserId!.Value;
        row.EditedAt = now;
        ctx.Submission.LastEditedBy = current.UserId;
        ctx.Submission.LastEditedAt = now;
        if (ctx.Submission.Status == SubmissionStatus.NotStarted) ctx.Submission.Status = SubmissionStatus.InProgress;

        var imported = verb == "dc.value_imported";
        audit.Record(AuditEvents.Audit("dc", Feature, verb, imported ? "استيراد قيمة مؤشر من Excel" : "حفظ قيمة مؤشر في نموذج جهة",
            imported ? "Imported an indicator value" : "Saved an indicator value") with
        {
            Target = new AuditTarget("submitted_value", row.Id.ToString(), ci.Ci.Code, ctx.Unit.Code),
            Related = [new AuditTarget("submission", ctx.Submission.Id.ToString(), ctx.Unit.Code)],
            Changes = changes.ToList(),
        });
        return row;
    }

    /// <summary>مسح قيمة لم ترسل بعد (الإدخال الخاطئ قبل الإرسال)؛ لا يمس ما أرسل أو اعتمد.</summary>
    public async Task ClearValueAsync(Guid id, Guid indicatorId, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.Edit, tracking: true, ct);
        EnsureEditable(ctx);
        if (!ctx.Values.TryGetValue(indicatorId, out var row)) return;
        if (await db.Documents.AnyAsync(d => d.OwnerType == StoredDocument.ValueOwner && d.OwnerId == row.Id && d.VoidedAt == null, ct))
            throw new DomainException("has_evidence", "أبطل الأدلة المرفقة بهذه القيمة أولًا.", "Void the attached evidence first.");
        db.SubmittedValues.Remove(row);
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.value_cleared", "مسح قيمة غير مرسلة", "Cleared an unsent value") with
        {
            Target = new AuditTarget("submission", ctx.Submission.Id.ToString(), ctx.Unit.Code),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<FormDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.Edit, tracking: true, ct);
        EnsureEditable(ctx);
        var blocking = await BlockingAsync(ctx, ct);
        if (blocking.Count > 0)
            throw new DomainException("not_ready", "لا يرسل النموذج قبل استكمال: " + string.Join(" — ", blocking.Take(3)) + (blocking.Count > 3 ? $" (و{blocking.Count - 3} غيرها)" : ""), "The form is not ready.");
        var next = ApprovalRules.Next(ctx.Unit.Kind, ApprovalStep.Entry);
        Move(ctx, SubmissionAction.Submitted, next, null);
        foreach (var v in ctx.Values.Values) v.ReviewNote = null;
        ctx.Submission.SubmittedAt = clock.UtcNow;
        ctx.Submission.ReturnNote = null;
        await db.SaveChangesAsync(ct);
        await notifier.MovedAsync(ctx.Submission, ctx.Unit, ctx.Cycle, ct);
        return await BuildAsync(ctx, ct);
    }

    public async Task<FormDto> ApproveAsync(Guid id, NoteRequest req, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.View, tracking: true, ct);
        await EnsureReviewerAsync(ctx, ct);
        var next = ApprovalRules.Next(ctx.Unit.Kind, ctx.Submission.Step);
        Move(ctx, SubmissionAction.Approved, next, Require.Optional(req.Note, 2000));
        await db.SaveChangesAsync(ct);
        await notifier.MovedAsync(ctx.Submission, ctx.Unit, ctx.Cycle, ct);
        return await BuildAsync(ctx, ct);
    }

    /// <summary>الإعادة للتصحيح بسبب، مع ملاحظة على كل مؤشر يحتاج تعديلًا فيبرز لمدخل البيانات.</summary>
    public async Task<FormDto> ReturnAsync(Guid id, ReturnRequest req, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.View, tracking: true, ct);
        await EnsureReviewerAsync(ctx, ct);
        var note = Require.Reason(req.Note, 5, "note_required", 2000);
        foreach (var item in req.Items ?? [])
            if (ctx.Values.TryGetValue(item.IndicatorId, out var v)) v.ReviewNote = Require.Optional(item.Note, 1000);
        Move(ctx, SubmissionAction.Returned, ApprovalStep.Entry, note);
        ctx.Submission.ReturnNote = note;
        await db.SaveChangesAsync(ct);
        await notifier.MovedAsync(ctx.Submission, ctx.Unit, ctx.Cycle, ct);
        return await BuildAsync(ctx, ct);
    }

    /// <summary>إعادة فتح نموذج معتمد بطلب مسبب وصلاحية خاصة؛ القيم السابقة ومراحل اعتمادها تبقى في السجل.</summary>
    public async Task<FormDto> ReopenAsync(Guid id, NoteRequest req, CancellationToken ct)
    {
        await access.RequireSpecialAsync(Features.Cycles, ApprovalRules.Reopen, ct);
        var ctx = await LoadAsync(id, Cap.View, tracking: true, ct);
        var reason = Require.Reason(req.Note);
        if (ctx.Submission.Status != SubmissionStatus.Approved) throw new DomainException("not_approved", "يعاد فتح النموذج المعتمد فقط.", "Only an approved form can be reopened.");
        if (ctx.Cycle.Status != CycleStatus.Open) throw new DomainException("cycle_not_open", "الدورة مغلقة.", "The cycle is closed.");
        Move(ctx, SubmissionAction.Reopened, ApprovalStep.Entry, reason);
        ctx.Submission.ApprovedAt = null;
        ctx.Submission.ReturnNote = reason;
        await db.SaveChangesAsync(ct);
        await notifier.MovedAsync(ctx.Submission, ctx.Unit, ctx.Cycle, ct);
        return await BuildAsync(ctx, ct);
    }

    /// <summary>تمديد موعد جهة بعينها بقرار مسجل دون تمديد الدورة للجميع (ضوابط التجربة 8-1 بند 7).</summary>
    public async Task<FormDto> ExtendAsync(Guid id, ExtendRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Features.Cycles, Cap.Edit, ct);
        var ctx = await LoadAsync(id, Cap.View, tracking: true, ct);
        var reason = Require.Reason(req.Reason);
        if (ctx.Cycle.Status != CycleStatus.Open) throw new DomainException("cycle_not_open", "الدورة غير مفتوحة.", "The cycle is not open.");
        if (ctx.Submission.Status == SubmissionStatus.Approved) throw new DomainException("form_approved", "النموذج معتمد؛ لا يحتاج تمديدًا.", "The form is approved.");
        if (req.DueOn <= ctx.Submission.DueOn)
            throw new DomainException("due_not_later", "الموعد الجديد بعد الموعد الحالي.", "The new deadline must be later.", 400);
        var old = ctx.Submission.DueOn;
        ctx.Submission.DueOn = req.DueOn;
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.form_extended", "تمديد موعد نموذج جهة", "Extended a unit form deadline") with
        {
            Target = new AuditTarget("submission", ctx.Submission.Id.ToString(), ctx.Unit.Code, ctx.Unit.Code),
            Reason = reason,
            Changes = new ChangeSet().Add("dueOn", old.ToString("yyyy-MM-dd"), req.DueOn.ToString("yyyy-MM-dd")).ToList(),
        });
        await db.SaveChangesAsync(ct);
        return await BuildAsync(ctx, ct);
    }

    // ----------------------------------------------------------------- داخلي

    internal sealed record IndicatorInCycle(CycleIndicator Ci, IndicatorVersion Version, Standard Standard, int SortOrder);

    internal sealed class FormContext
    {
        public required Submission Submission { get; init; }
        public required CollectionCycle Cycle { get; init; }
        public required OrgUnit Unit { get; init; }
        public required List<IndicatorInCycle> Indicators { get; init; }
        public required Dictionary<Guid, SubmittedValue> Values { get; init; }
        public required Dictionary<Guid, decimal?> Previous { get; init; }
        public required EffectiveAccess Me { get; init; }
    }

    public static bool Applies(CycleIndicator ci, OrgUnit unit) => unit.Kind switch
    {
        OrgUnitKind.Department => ci.Level == CollectionLevel.Department,
        OrgUnitKind.College or OrgUnitKind.Institute => ci.Level == CollectionLevel.College || ci.CentralUnitId == unit.Id,
        _ => ci.CentralUnitId == unit.Id,
    };

    internal async Task<FormContext> LoadAsync(Guid id, Cap cap, bool tracking, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, cap, ct);
        var subQuery = tracking ? db.Submissions : db.Submissions.AsNoTracking();
        var sub = await subQuery.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound();
        var unit = await db.OrgUnits.AsNoTracking().FirstAsync(u => u.Id == sub.UnitId, ct);
        UnitScope.For(me, Feature, cap).Require(unit);
        var cycle = await db.Cycles.AsNoTracking().FirstAsync(c => c.Id == sub.CycleId, ct);

        var cis = (await db.CycleIndicators.AsNoTracking().Where(c => c.CycleId == cycle.Id).ToListAsync(ct)).Where(c => Applies(c, unit)).ToList();
        var versionIds = cis.Select(c => c.VersionId).ToList();
        var versions = await db.IndicatorVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var indicatorIds = cis.Select(c => c.IndicatorId).ToList();
        var meta = await db.Indicators.AsNoTracking().Where(i => indicatorIds.Contains(i.Id)).Select(i => new { i.Id, i.StandardId, i.SortOrder }).ToDictionaryAsync(i => i.Id, ct);
        var standards = await db.Standards.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var valuesQuery = tracking ? db.SubmittedValues : db.SubmittedValues.AsNoTracking();
        var values = await valuesQuery.Where(v => v.SubmissionId == sub.Id).ToDictionaryAsync(v => v.IndicatorId, ct);

        // آخر قيمة معتمدة للجهة نفسها في دورة سابقة، لتنبيه الاختلاف الكبير وللمقارنة. الدورة الفعلية لا تقارن بقيم البروفة.
        var rehearsal = cycle.Trial;
        var previous = (await (from v in db.SubmittedValues.AsNoTracking()
                               join s in db.Submissions.AsNoTracking() on v.SubmissionId equals s.Id
                               join c in db.Cycles.AsNoTracking() on s.CycleId equals c.Id
                               where s.UnitId == unit.Id && s.Status == SubmissionStatus.Approved && c.Id != cycle.Id && c.OpenedAt < cycle.OpenedAt
                                     && (rehearsal || !c.Trial)
                               select new { v.IndicatorId, v.Value, c.OpenedAt }).ToListAsync(ct))
            .GroupBy(x => x.IndicatorId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OpenedAt).First().Value);

        return new FormContext
        {
            Submission = sub,
            Cycle = cycle,
            Unit = unit,
            Indicators = cis.Select(c => new IndicatorInCycle(c, versions[c.VersionId], standards[meta[c.IndicatorId].StandardId], meta[c.IndicatorId].SortOrder))
                .OrderBy(x => x.Standard.SortOrder).ThenBy(x => x.SortOrder).ToList(),
            Values = values,
            Previous = previous,
            Me = me,
        };
    }

    private void EnsureEditable(FormContext ctx)
    {
        if (ctx.Cycle.Status != CycleStatus.Open)
            throw new DomainException("cycle_not_open", "الدورة غير مفتوحة للإدخال.", "The cycle is not open.");
        if (!ctx.Submission.IsEditable)
            throw new DomainException("form_locked", ctx.Submission.Status == SubmissionStatus.Approved
                ? "النموذج معتمد ومقفل؛ تعديله يحتاج إعادة فتح مسببة."
                : $"النموذج عند «{ApprovalRules.StepName(ctx.Submission.Step)}»؛ لا يعدل حتى يعاد للتصحيح.", "The form is locked.", 409);
    }

    private async Task EnsureReviewerAsync(FormContext ctx, CancellationToken ct)
    {
        if (ctx.Cycle.Status != CycleStatus.Open) throw new DomainException("cycle_not_open", "الدورة مغلقة.", "The cycle is closed.");
        if (ctx.Submission.Status != SubmissionStatus.InReview)
            throw new DomainException("not_in_review", "النموذج لا ينتظر مراجعة الآن.", "The form is not waiting for review.");
        var special = ApprovalRules.SpecialFor(ctx.Submission.Step);
        if (!ctx.Me.HasSpecial(Feature, special)) throw DomainException.Forbidden();
        var history = await db.SubmissionEvents.AsNoTracking().Where(e => e.SubmissionId == ctx.Submission.Id && e.At > (ctx.Submission.SubmittedAt ?? DateTimeOffset.MinValue).AddSeconds(-1)).ToListAsync(ct);
        ApprovalRules.EnsureSeparation(current.UserId!.Value, ctx.Values.Values.Select(v => v.EditedBy), history);
    }

    private void Move(FormContext ctx, SubmissionAction action, ApprovalStep to, string? note)
    {
        var s = ctx.Submission;
        var from = s.Step;
        db.SubmissionEvents.Add(new SubmissionEvent
        {
            SubmissionId = s.Id, Action = action, FromStep = from, ToStep = to, By = current.UserId!.Value, ByName = current.DisplayName, At = clock.UtcNow, Note = note,
        });
        s.Step = to;
        s.Status = to switch
        {
            ApprovalStep.Done => SubmissionStatus.Approved,
            ApprovalStep.Entry => action == SubmissionAction.Returned ? SubmissionStatus.Returned : SubmissionStatus.InProgress,
            _ => SubmissionStatus.InReview,
        };
        if (s.Status == SubmissionStatus.Approved) s.ApprovedAt = clock.UtcNow;
        var (verb, ar, en) = action switch
        {
            SubmissionAction.Submitted => ("dc.form_submitted", "إرسال نموذج جهة للمراجعة", "Submitted a unit form"),
            SubmissionAction.Approved => (to == ApprovalStep.Done ? "dc.form_approved_final" : "dc.form_approved", to == ApprovalStep.Done ? "الاعتماد النهائي لنموذج جهة" : "اعتماد خطوة في نموذج جهة", "Approved a unit form step"),
            SubmissionAction.Returned => ("dc.form_returned", "إعادة نموذج جهة للتصحيح", "Returned a unit form"),
            _ => ("dc.form_reopened", "إعادة فتح نموذج معتمد", "Reopened an approved form"),
        };
        audit.Record(AuditEvents.Audit("dc", Feature, verb, ar, en) with
        {
            Target = new AuditTarget("submission", s.Id.ToString(), ctx.Unit.Code, ctx.Unit.Code),
            Reason = note,
            Details = new { from = ApprovalRules.StepName(from), to = ApprovalRules.StepName(to), cycle = ctx.Cycle.Name },
        });
    }

    private async Task<List<string>> BlockingAsync(FormContext ctx, CancellationToken ct)
    {
        var evidence = await EvidenceCountsAsync(ctx, ct);
        var bigChange = await settings.IntAsync(SettingKeys.BigChangePercent, ct) / 100m;
        return ValueRules.ReadyToSubmit(
                ctx.Indicators.Select(i => (i.Ci.IndicatorId, i.Ci.Code, CycleSpec.Of(i.Version))).ToList(), ctx.Values, evidence, ctx.Previous, bigChange)
            .Select(b => b.Issue.MessageAr).ToList();
    }

    private async Task<Dictionary<Guid, int>> EvidenceCountsAsync(FormContext ctx, CancellationToken ct)
    {
        var valueIds = ctx.Values.Values.Select(v => v.Id).ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.ValueOwner && valueIds.Contains(d.OwnerId) && d.VoidedAt == null)
            .GroupBy(d => d.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byValue = ctx.Values.Values.ToDictionary(v => v.Id, v => v.IndicatorId);
        return docs.ToDictionary(d => byValue[d.Key], d => d.Count);
    }

    private async Task<Dictionary<Guid, decimal?>> TargetsAsync(FormContext ctx, CancellationToken ct)
    {
        if (ctx.Unit.Kind == OrgUnitKind.Department) return [];
        var ids = ctx.Indicators.Select(i => i.Ci.IndicatorId).ToList();
        var targets = await db.Targets.AsNoTracking().Where(t => ids.Contains(t.IndicatorId) && t.Year == ctx.Cycle.TargetYear).ToListAsync(ct);
        var result = new Dictionary<Guid, decimal?>();
        foreach (var id in ids)
        {
            var own = targets.FirstOrDefault(t => t.IndicatorId == id && t.UnitId == ctx.Unit.Id && t.Kind == TargetKind.Target);
            var each = ctx.Unit.IsCollegeLevel ? targets.FirstOrDefault(t => t.IndicatorId == id && t.Kind == TargetKind.EachCollege) : null;
            result[id] = own?.Value ?? each?.Value;
        }
        return result;
    }

    private async Task<FormItem> ItemAsync(FormContext ctx, IndicatorInCycle i, decimal bigChange, CancellationToken ct)
    {
        var targets = await TargetsAsync(ctx, ct);
        var docs = await DocsAsync(ctx, ct);
        return Item(ctx, i, bigChange, targets, docs);
    }

    private async Task<Dictionary<Guid, List<DocDto>>> DocsAsync(FormContext ctx, CancellationToken ct)
    {
        var valueIds = ctx.Values.Values.Select(v => v.Id).ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.ValueOwner && valueIds.Contains(d.OwnerId))
            .OrderBy(d => d.UploadedAt).ToListAsync(ct);
        var byValue = ctx.Values.Values.ToDictionary(v => v.Id, v => v.IndicatorId);
        return docs.GroupBy(d => byValue[d.OwnerId]).ToDictionary(g => g.Key, g => g.Select(ToDoc).ToList());
    }

    private static FormItem Item(FormContext ctx, IndicatorInCycle i, decimal bigChange, Dictionary<Guid, decimal?> targets, Dictionary<Guid, List<DocDto>> docs)
    {
        var v = i.Version;
        var spec = CycleSpec.Of(v);
        ctx.Values.TryGetValue(i.Ci.IndicatorId, out var value);
        IReadOnlyList<IssueDto> issues = [];
        if (value is not null)
        {
            var (_, found) = ValueRules.Check(spec, new ValueInput(value.Kind, value.Value, value.Numerator, value.Denominator, value.Reason, value.Explanation),
                ctx.Previous.GetValueOrDefault(i.Ci.IndicatorId), bigChange);
            issues = found.Select(f => new IssueDto(f.Code, f.Severity, f.MessageAr)).ToList();
        }
        return new FormItem(i.Ci.IndicatorId, i.Ci.Code, v.NameAr, v.Unit, v.Aggregation, ValueRules.UsesRatio(spec), v.Numerator, v.Denominator,
            v.Definition, v.DataSource, v.EvidenceRequired, v.Direction, ctx.Previous.GetValueOrDefault(i.Ci.IndicatorId), targets.GetValueOrDefault(i.Ci.IndicatorId),
            value is null ? null : new ValueDto(value.Kind, value.Value, value.Numerator, value.Denominator, value.Reason, value.Explanation, value.ReviewNote, value.EditedAt, value.Version),
            issues, docs.GetValueOrDefault(i.Ci.IndicatorId) ?? []);
    }

    internal static DocDto ToDoc(StoredDocument d) => new(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt, d.Description, d.VoidedAt is not null, d.VoidReason);

    private async Task<FormDto> BuildAsync(FormContext ctx, CancellationToken ct)
    {
        var s = ctx.Submission;
        var bigChange = await settings.IntAsync(SettingKeys.BigChangePercent, ct) / 100m;
        var targets = await TargetsAsync(ctx, ct);
        var docs = await DocsAsync(ctx, ct);
        var events = await db.SubmissionEvents.AsNoTracking().Where(e => e.SubmissionId == s.Id).OrderBy(e => e.At).ToListAsync(ct);
        var path = ApprovalRules.PathFor(ctx.Unit.Kind);
        var stepIndex = s.Status == SubmissionStatus.Approved ? path.Count : path.ToList().IndexOf(s.Step);
        var blocking = s.IsEditable ? await BlockingAsync(ctx, ct) : [];

        var cycleOpen = ctx.Cycle.Status == CycleStatus.Open;
        var canEdit = cycleOpen && s.IsEditable && ctx.Me.Has(Feature, Cap.Edit) && UnitScope.For(ctx.Me, Feature, Cap.Edit).Covers(ctx.Unit);
        var sinceSubmit = events.Where(e => e.At >= (s.SubmittedAt ?? DateTimeOffset.MaxValue).AddSeconds(-1)).ToList();
        var canReview = cycleOpen && s.Status == SubmissionStatus.InReview && ctx.Me.HasSpecial(Feature, ApprovalRules.SpecialFor(s.Step))
                        && ctx.Values.Values.All(v => v.EditedBy != current.UserId)
                        && !sinceSubmit.Any(e => e.Action == SubmissionAction.Approved && e.By == current.UserId);
        var canReopen = cycleOpen && s.Status == SubmissionStatus.Approved && ctx.Me.HasSpecial(Features.Cycles, ApprovalRules.Reopen);
        var next = s.Status == SubmissionStatus.Approved ? null
            : s.IsEditable ? ApprovalRules.StepName(ApprovalRules.Next(ctx.Unit.Kind, ApprovalStep.Entry))
            : ApprovalRules.StepName(ApprovalRules.Next(ctx.Unit.Kind, s.Step));

        return new FormDto(
            s.Id, ctx.Cycle.Id, ctx.Cycle.Name, ctx.Cycle.PeriodLabel, ctx.Cycle.Status, ctx.Cycle.Trial, ctx.Cycle.TargetYear,
            ctx.Unit.Id, ctx.Unit.NameAr, ctx.Unit.Kind, s.Step, s.Status, s.DueOn,
            cycleOpen && s.Status != SubmissionStatus.Approved && s.DueOn < CairoTime.DateOf(clock.UtcNow),
            path.Select((p, i) => new PathStep(p, ApprovalRules.StepName(p), i < stepIndex ? "done" : i == stepIndex ? "current" : "pending")).ToList(),
            next, s.ReturnNote, s.LastEditedAt, ctx.Values.Count, ctx.Indicators.Count,
            ctx.Indicators.GroupBy(i => i.Standard).Select(g => new FormSection(g.Key.Code, g.Key.NameAr, g.Select(i => Item(ctx, i, bigChange, targets, docs)).ToList())).ToList(),
            blocking,
            events.Select(e => new FormEventDto(e.Action, e.FromStep, e.ToStep, e.ByName, e.At, e.Note)).ToList(),
            new FormActions(canEdit, canEdit && blocking.Count == 0, canReview, canReopen),
            s.Version);
    }
}
