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

internal sealed record IndicatorInCycle(CycleIndicator Ci, IndicatorVersion Version, Standard Standard, int SortOrder, Pillar? Pillar, int? PlanStart);

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

/// <summary>
/// ARCH-1: one form as its screen shows it. Loads the form's context (unit, cycle, indicators, values, previous values) in the
/// user's scope, and builds the form, an item, the blocking list, targets and documents. Shared by the list, entry and workflow.
/// </summary>
public sealed class FormReader(ICollectionStore db, IReadModel read,  IClock clock, ICurrentUser current, IAccessService access, Settings.SettingsService settings)
{
    private const string Feature = Features.Forms;

    internal async Task<FormContext> LoadAsync(Guid id, Cap cap, bool tracking, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, cap, ct);
        var subQuery = tracking ? db.Submissions : db.Submissions.AsNoTracking();
        var sub = await subQuery.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound();
        var unit = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstAsync(u => u.Id == sub.UnitId, ct);
        UnitScope.For(me, Feature, cap).Require(unit);
        var cycle = await db.Cycles.AsNoTracking().FirstAsync(c => c.Id == sub.CycleId, ct);

        var cis = (await db.CycleIndicators.AsNoTracking().Where(c => c.CycleId == cycle.Id).ToListAsync(ct)).Where(c => FormAssignmentRules.Applies(c, unit)).ToList();
        var versionIds = cis.Select(c => c.VersionId).ToList();
        var versions = await read.Query<Domain.Kpi.IndicatorVersion>().AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var indicatorIds = cis.Select(c => c.IndicatorId).ToList();
        var meta = await read.Query<Domain.Kpi.Indicator>().AsNoTracking().Where(i => indicatorIds.Contains(i.Id)).Select(i => new { i.Id, i.StandardId, i.SortOrder, i.PlanStartYear }).ToDictionaryAsync(i => i.Id, ct);
        var standards = await read.Query<Domain.Kpi.Standard>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var pillars = await read.Query<Domain.Kpi.Pillar>().AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
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
            Indicators = cis.Select(c =>
                {
                    var std = standards[meta[c.IndicatorId].StandardId];
                    return new IndicatorInCycle(c, versions[c.VersionId], std, meta[c.IndicatorId].SortOrder,
                        std.PillarId is { } p ? pillars.GetValueOrDefault(p) : null, meta[c.IndicatorId].PlanStartYear);
                })
                .OrderBy(x => x.Pillar?.SortOrder ?? int.MaxValue).ThenBy(x => x.Standard.SortOrder).ThenBy(x => x.SortOrder).ToList(),
            Values = values,
            Previous = previous,
            Me = me,
        };
    }

    internal static void EnsureEditable(FormContext ctx)
    {
        if (ctx.Cycle.Status != CycleStatus.Open)
            throw new DomainException("cycle_not_open", "الدورة غير مفتوحة للإدخال.", "The cycle is not open.");
        if (!ctx.Submission.IsEditable)
            throw new DomainException("form_locked", ctx.Submission.Status == SubmissionStatus.Approved
                ? "النموذج معتمد ومقفل؛ تعديله يحتاج إعادة فتح مسببة."
                : $"النموذج عند «{ApprovalRules.StepName(ctx.Submission.Step)}»؛ لا يعدل حتى يعاد للتصحيح.", "The form is locked.", 409);
    }


    internal async Task<List<string>> BlockingAsync(FormContext ctx, CancellationToken ct)
    {
        var evidence = await EvidenceCountsAsync(ctx, ct);
        var bigChange = await settings.IntAsync(SettingKeys.BigChangePercent, ct) / 100m;
        return ValueRules.ReadyToSubmit(
                ctx.Indicators.Select(i => (i.Ci.IndicatorId, i.Ci.Code, CycleSpec.Of(i.Version))).ToList(), ctx.Values, evidence, ctx.Previous, bigChange)
            .Select(b => b.Issue.MessageAr).ToList();
    }

    internal async Task<Dictionary<Guid, int>> EvidenceCountsAsync(FormContext ctx, CancellationToken ct)
    {
        var valueIds = ctx.Values.Values.Select(v => v.Id).ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.ValueOwner && valueIds.Contains(d.OwnerId) && d.VoidedAt == null)
            .GroupBy(d => d.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byValue = ctx.Values.Values.ToDictionary(v => v.Id, v => v.IndicatorId);
        return docs.ToDictionary(d => byValue[d.Key], d => d.Count);
    }

    /// <summary>المستهدف الذي يظهر بجانب كل مؤشر، بقواعد النتائج نفسها (سنة البدء، واستمرار آخر مستهدف، ومستهدف الجامعة للنسب).</summary>
    internal async Task<Dictionary<Guid, Kpi.TargetInfo>> TargetsAsync(FormContext ctx, CancellationToken ct)
    {
        var ids = ctx.Indicators.Select(i => i.Ci.IndicatorId).ToList();
        var targets = (await read.Query<Domain.Kpi.IndicatorTarget>().AsNoTracking().Where(t => ids.Contains(t.IndicatorId) && t.Year <= ctx.Cycle.TargetYear && t.Kind != TargetKind.Baseline).ToListAsync(ct))
            .ToLookup(t => t.IndicatorId);
        var universityId = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Kind == OrgUnitKind.University).Select(u => u.Id).FirstAsync(ct);
        return ctx.Indicators.ToDictionary(i => i.Ci.IndicatorId,
            i => Kpi.TargetLookup.For([.. targets[i.Ci.IndicatorId]], ctx.Unit, universityId, ctx.Cycle.TargetYear, i.PlanStart, i.Version.Aggregation));
    }

    internal async Task<FormItem> ItemAsync(FormContext ctx, IndicatorInCycle i, decimal bigChange, CancellationToken ct)
    {
        var targets = await TargetsAsync(ctx, ct);
        var docs = await DocsAsync(ctx, ct);
        return Item(ctx, i, bigChange, targets, docs);
    }

    internal async Task<Dictionary<Guid, List<DocDto>>> DocsAsync(FormContext ctx, CancellationToken ct)
    {
        var valueIds = ctx.Values.Values.Select(v => v.Id).ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.ValueOwner && valueIds.Contains(d.OwnerId))
            .OrderBy(d => d.UploadedAt).ToListAsync(ct);
        var byValue = ctx.Values.Values.ToDictionary(v => v.Id, v => v.IndicatorId);
        return docs.GroupBy(d => byValue[d.OwnerId]).ToDictionary(g => g.Key, g => g.Select(ToDoc).ToList());
    }

    private static FormItem Item(FormContext ctx, IndicatorInCycle i, decimal bigChange, Dictionary<Guid, Kpi.TargetInfo> targets, Dictionary<Guid, List<DocDto>> docs,
        IReadOnlyDictionary<Guid, ApprovedValueDto>? before = null)
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
        var target = targets.GetValueOrDefault(i.Ci.IndicatorId) ?? Kpi.TargetInfo.None;
        return new FormItem(i.Ci.IndicatorId, i.Ci.Code, v.NameAr, v.Unit, v.Aggregation, ValueRules.UsesRatio(spec), v.Numerator, v.Denominator,
            v.Definition, v.DataSource, v.EvidenceRequired, v.Direction, ctx.Previous.GetValueOrDefault(i.Ci.IndicatorId), target.Value,
            value is null ? null : new ValueDto(value.Kind, value.Value, value.Numerator, value.Denominator, value.Reason, value.Explanation, value.ReviewNote, value.EditedAt, value.Version),
            issues, docs.GetValueOrDefault(i.Ci.IndicatorId) ?? [], FormAssignmentRules.EntryNote(i.Ci, v, ctx.Unit), target.Source, target.PendingValue, target.PendingYear,
            before?.GetValueOrDefault(i.Ci.IndicatorId));
    }

    internal static DocDto ToDoc(StoredDocument d) => new(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt, d.Description, d.VoidedAt is not null, d.VoidReason);

    internal async Task<FormDto> BuildAsync(FormContext ctx, CancellationToken ct)
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
        var canReview = cycleOpen && ApprovalRules.CanDecide(ctx.Me, ctx.Unit, s, current.UserId!.Value, ctx.Values.Values.Select(v => v.EditedBy), events);
        // D4: طلب إعادة الفتح ممن يملكه ولا طلب معلق، وقرار الطلب المعلق للجنة العليا ما لم يكن مقدمه.
        var (reopen, pendingBy, before) = await ReopenStateAsync(s, ct);
        var canReopen = cycleOpen && s.Status == SubmissionStatus.Approved && ctx.Me.HasSpecial(Features.Cycles, ApprovalRules.Reopen) && pendingBy is null;
        var canDecideReopen = pendingBy is { } by && by != current.UserId && ctx.Me.HasSpecial(Features.Cycles, ApprovalRules.ApproveReopen);
        var directTo = canReview ? ApprovalRules.DirectApprovalTo(ctx.Me, ctx.Unit, s.Step) : null;
        var canApprove = canReview && ApprovalRules.MayApprove(ctx.Me, ctx.Unit, s.Step);
        var next = s.Status == SubmissionStatus.Approved ? null
            : ApprovalRules.StepName(ApprovalRules.Next(ctx.Unit.Kind, s.Step));

        return new FormDto(
            s.Id, ctx.Cycle.Id, ctx.Cycle.Name, ctx.Cycle.PeriodLabel, ctx.Cycle.Status, ctx.Cycle.Trial, ctx.Cycle.TargetYear,
            ctx.Unit.Id, ctx.Unit.NameAr, ctx.Unit.Kind, s.Step, s.Status, s.DueOn,
            cycleOpen && s.Status != SubmissionStatus.Approved && s.DueOn < CairoTime.DateOf(clock.UtcNow),
            path.Select((p, i) => new PathStep(p, ApprovalRules.StepName(p), i < stepIndex && events.Any(e => e.At >= s.SubmittedAt && ApprovalRules.SkippedBy(e, p))
                ? "skipped" : i < stepIndex ? "done" : i == stepIndex ? "current" : "pending")).ToList(),
            next, s.ReturnNote, s.LastEditedAt, ctx.Values.Count, ctx.Indicators.Count,
            ctx.Indicators.GroupBy(i => i.Standard)
                .Select(g => new FormSection(g.First().Pillar?.Code, g.First().Pillar?.NameAr, g.Key.Code, g.Key.NameAr, g.Select(i => Item(ctx, i, bigChange, targets, docs, before)).ToList()))
                .ToList(),
            blocking,
            events.Select(e => new FormEventDto(e.Action, e.FromStep, e.ToStep, e.ByName, e.At, e.Note)).ToList(),
            new FormActions(canEdit, canEdit && blocking.Count == 0, canReview, canReopen, directTo == ApprovalStep.Dean, directTo == ApprovalStep.Quality,
                canApprove, directTo is { } d ? ApprovalRules.StepName(d) : null, canDecideReopen),
            s.Version, reopen);
    }

    /// <summary>آخر طلب للنموذج، وقيمه المعتمدة قبل إعادة الفتح إن كان قيد التصحيح.</summary>
    internal async Task<(FormReopenDto? Latest, Guid? PendingBy, Dictionary<Guid, ApprovedValueDto> Before)> ReopenStateAsync(Submission s, CancellationToken ct)
    {
        var requests = await db.ReopenRequests.AsNoTracking().Where(r => r.SubmissionId == s.Id).ToListAsync(ct);
        var latest = requests.OrderByDescending(r => r.RequestedAt).FirstOrDefault();
        var official = ReopenRules.OfficialSnapshot(s, requests);
        var before = official is { } rid
            ? await db.ValueSnapshots.AsNoTracking().Where(v => v.RequestId == rid)
                .ToDictionaryAsync(v => v.IndicatorId, v => new ApprovedValueDto(v.Kind, v.Value, v.Numerator, v.Denominator, v.Reason, v.ApprovedAt), ct)
            : [];
        return (latest is null ? null : new FormReopenDto(latest.Id, latest.Status, latest.Reason, latest.RequestedByName, latest.RequestedAt,
            latest.DecidedByName, latest.DecidedAt, latest.DecisionRef, latest.DecisionNote),
            latest?.Status == ReopenRequestStatus.Pending ? latest.RequestedBy : null, before);
    }
}
