using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Kpi;

public sealed record TargetDto(
    Guid Id, Guid UnitId, string UnitCode, string UnitName, int Year, TargetKind Kind, decimal? Value, string? Text,
    decimal? Numerator, decimal? Denominator, string? Source, string? Note, TargetStatus Status, string? DecisionRef, uint Version);

public sealed record TargetRevisionDto(decimal? OldValue, string? OldText, decimal? NewValue, string? NewText, string Reason, string? DecisionRef, DateTimeOffset ChangedAt);

public sealed record IndicatorTargetsDto(
    string IndicatorCode, string Unit, Direction Direction, Aggregation Aggregation, CollectionLevel Level,
    IReadOnlyList<int> Years, IReadOnlyList<TargetDto> University, TargetDto? EachCollege, IReadOnlyList<TargetDto> Colleges,
    IReadOnlyList<TargetRevisionDto> Revisions, TargetActions Actions);

public sealed record TargetActions(bool CanEditUniversity, bool CanEditColleges, bool CanApprove);

public sealed record SaveTargetRequest(
    Guid UnitId, int Year, TargetKind Kind, decimal? Value, string? Text, decimal? Numerator, decimal? Denominator,
    string? Source, string? Note, string? Reason, string? DecisionRef, uint? Version);

public sealed record DistributionDto(int Year, DistributionResult Result, IReadOnlyList<CollegeShare> Shares);

public sealed record PlanValue(int Year, decimal? Value);

/// <summary>
/// خطة مستهدف المؤشر للجامعة: سنة بدئه وسنة تحقيقه وشرحه، وقيمة كل سنة. القيمة الفارغة تحذف مستهدف تلك السنة.
/// تعديل مستهدف معتمد أو حذفه يحتاج صلاحية الاعتماد وسببًا ورقم قرار، كأي تعديل على معتمد.
/// </summary>
public sealed record SavePlanRequest(int? StartYear, int? EndYear, string? Note, IReadOnlyList<PlanValue>? Values, string? Reason, string? DecisionRef, uint? Version);

public sealed record DeleteTargetRequest(string? Reason, string? DecisionRef);

/// <summary>
/// خط الأساس والمستهدفات: مستهدفات الجامعة (من ملف Tier 1) وتوزيعها على الكليات (من ملف الكليات)، والاعتماد بقرار،
/// وتعديل المعتمد بسبب ورقم قرار مع حفظ القيمة السابقة.
/// </summary>
public sealed class TargetsService(IAppDb db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit)
{
    public const int PlanStart = 2026;
    public const int PlanEnd = 2031;
    private const string Feature = Features.Targets;

    public async Task<IndicatorTargetsDto> GetAsync(string code, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var (indicator, version) = await LoadIndicatorAsync(code, ct);
        var university = await UniversityAsync(ct);
        var viewScope = UnitScope.For(me, Feature, Cap.View);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var targets = await db.Targets.AsNoTracking().Where(t => t.IndicatorId == indicator.Id).ToListAsync(ct);
        var ids = targets.Select(t => t.Id).ToList();
        var revisions = await db.TargetRevisions.AsNoTracking().Where(r => ids.Contains(r.TargetId)).OrderByDescending(r => r.ChangedAt).Take(50).ToListAsync(ct);

        TargetDto Dto(IndicatorTarget t) => ToDto(t, units[t.UnitId]);
        var editScope = UnitScope.For(me, Feature, Cap.Edit);
        return new IndicatorTargetsDto(
            indicator.Code, version.Unit, version.Direction, version.Aggregation, version.Level,
            Enumerable.Range(PlanStart, PlanEnd - PlanStart + 1).ToList(),
            targets.Where(t => t.UnitId == university.Id && t.Kind != TargetKind.EachCollege).OrderBy(t => t.Year).Select(Dto).ToList(),
            targets.FirstOrDefault(t => t.Kind == TargetKind.EachCollege) is { } each ? Dto(each) : null,
            targets.Where(t => t.UnitId != university.Id && viewScope.Covers(units[t.UnitId])).OrderBy(t => units[t.UnitId].NameAr).ThenBy(t => t.Year).Select(Dto).ToList(),
            revisions.Select(r => new TargetRevisionDto(r.OldValue, r.OldText, r.NewValue, r.NewText, r.Reason, r.DecisionRef, r.ChangedAt)).ToList(),
            new TargetActions(editScope.All, me.Has(Feature, Cap.Edit), me.HasSpecial(Feature, Specials.Approve)));
    }

    public async Task<TargetDto> SaveAsync(string code, SaveTargetRequest req, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.Edit, ct);
        var (indicator, version) = await LoadIndicatorAsync(code, ct);
        var unit = await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UnitId, ct) ?? throw DomainException.NotFound();
        UnitScope.For(me, Feature, Cap.Edit).Require(unit);
        var value = TargetRules.Validate(req, unit, version);

        var target = await db.Targets.FirstOrDefaultAsync(t => t.IndicatorId == indicator.Id && t.UnitId == unit.Id && t.Year == req.Year && t.Kind == req.Kind, ct);
        var now = clock.UtcNow;
        string? reason = null;
        if (target is null)
        {
            target = new IndicatorTarget { IndicatorId = indicator.Id, UnitId = unit.Id, Year = req.Year, Kind = req.Kind };
            db.Targets.Add(target);
        }
        else
        {
            Concurrency.Expect(db, target, req.Version);
            if (target.Status == TargetStatus.Approved)
            {
                // تعديل المعتمد: بصلاحية الاعتماد وسبب ورقم قرار، والقيمة السابقة تحفظ للمقارنة.
                if (!me.HasSpecial(Feature, Specials.Approve)) throw DomainException.Forbidden();
                reason = Require.Reason(req.Reason);
                var decision = Require.Optional(req.DecisionRef, 200)
                    ?? throw new DomainException("decision_ref_required", "تعديل مستهدف معتمد يحتاج رقم قرار اللجنة العليا.", "Changing an approved target needs a decision reference.", 400);
                db.TargetRevisions.Add(new TargetRevision
                {
                    TargetId = target.Id, OldValue = target.Value, OldText = target.Text, NewValue = value, NewText = Require.Optional(req.Text, 300),
                    Reason = reason, DecisionRef = decision, ChangedBy = current.UserId!.Value, ChangedAt = now,
                });
                target.DecisionRef = decision;
            }
        }

        var changes = new ChangeSet().Add("value", target.Value, value).Add("text", target.Text, Require.Optional(req.Text, 300))
            .Add("numerator", target.Numerator, req.Numerator).Add("denominator", target.Denominator, req.Denominator);
        target.Value = value;
        target.Text = Require.Optional(req.Text, 300);
        target.Numerator = req.Numerator;
        target.Denominator = req.Denominator;
        target.Source = Require.Optional(req.Source, 300);
        target.Note = Require.Optional(req.Note, 1000);
        target.UpdatedBy = current.UserId;
        target.UpdatedAt = now;

        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.target_saved", "حفظ مستهدف مؤشر", "Saved an indicator target") with
        {
            Target = new AuditTarget("indicator_target", target.Id.ToString(), $"{indicator.Code}/{unit.Code}/{req.Year}", unit.Code),
            Changes = changes.ToList(),
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(target, unit);
    }

    /// <summary>يحفظ خطة المؤشر وقيم سنواتها في حفظ واحد: إما كلها أو لا شيء.</summary>
    public async Task SavePlanAsync(string code, SavePlanRequest req, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.Edit, ct);
        var university = await UniversityAsync(ct);
        UnitScope.For(me, Feature, Cap.Edit).Require(university);
        var indicator = await db.Indicators.Include(i => i.Versions).FirstOrDefaultAsync(i => i.Code == code.Trim().ToUpperInvariant(), ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, indicator, req.Version);
        var version = indicator.Effective ?? indicator.Current;
        PlanRules.ValidateYears(req.StartYear, req.EndYear, PlanStart, "فترة المستهدف");

        var values = req.Values ?? [];
        if (values.GroupBy(v => v.Year).FirstOrDefault(g => g.Count() > 1) is { } dup)
            throw new DomainException("duplicate_year", $"السنة {dup.Key} مكررة.", "A year is repeated.", 400);
        foreach (var v in values.Where(v => v.Value is not null))
        {
            if (req.StartYear is { } s && v.Year < s)
                throw new DomainException("before_start", $"مستهدف {v.Year} قبل سنة بدء المؤشر ({s}). احذف القيمة أو قدّم سنة البدء.", "A target precedes the start year.", 400);
            if (v.Year <= PlanStart || v.Year > PlanEnd)
                throw new DomainException("invalid_year", $"سنوات المستهدفات من {PlanStart + 1} إلى {PlanEnd}.", "Target years are outside the plan.", 400);
        }

        var changes = new ChangeSet().Add("planStartYear", indicator.PlanStartYear, req.StartYear).Add("planEndYear", indicator.PlanEndYear, req.EndYear)
            .Add("planNote", indicator.PlanNote, Require.Optional(req.Note, 2000));
        indicator.PlanStartYear = req.StartYear;
        indicator.PlanEndYear = req.EndYear;
        indicator.PlanNote = Require.Optional(req.Note, 2000);

        var existing = await db.Targets.Where(t => t.IndicatorId == indicator.Id && t.UnitId == university.Id && t.Kind == TargetKind.Target).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var v in values)
        {
            var row = existing.FirstOrDefault(t => t.Year == v.Year);
            if (v.Value is null)
            {
                if (row is not null) Remove(row, me, req.Reason, req.DecisionRef, now);
                continue;
            }
            if (row is not null && row.Value == v.Value) continue;
            var save = new SaveTargetRequest(university.Id, v.Year, TargetKind.Target, v.Value, null, null, null, row?.Source ?? "خطة المؤشر", row?.Note, req.Reason, req.DecisionRef, null);
            var value = TargetRules.Validate(save, university, version);
            if (row is null)
            {
                db.Targets.Add(new IndicatorTarget
                {
                    IndicatorId = indicator.Id, UnitId = university.Id, Year = v.Year, Kind = TargetKind.Target, Value = value, Source = "خطة المؤشر",
                    UpdatedBy = current.UserId, UpdatedAt = now,
                });
                continue;
            }
            if (row.Status == TargetStatus.Approved) Revise(row, value, null, me, req.Reason, req.DecisionRef, now);
            row.Value = value;
            row.Text = null;
            row.UpdatedBy = current.UserId;
            row.UpdatedAt = now;
        }
        changes.Add("values", null, string.Join("، ", values.Select(v => $"{v.Year}: {(v.Value is { } x ? DistributionRules.Fmt(x) : "—")}")));

        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.target_plan_saved", "حفظ خطة مستهدف مؤشر", "Saved an indicator target plan") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Changes = changes.ToList(),
            Reason = Require.Optional(req.Reason, 500),
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>حذف مستهدف: المقترح بصلاحية الحذف، والمعتمد بصلاحية الاعتماد أيضًا وسبب ورقم قرار، وتحفظ قيمته في سجل التعديلات.</summary>
    public async Task DeleteAsync(Guid id, DeleteTargetRequest req, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.Delete, ct);
        var target = await db.Targets.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound();
        var unit = await db.OrgUnits.AsNoTracking().FirstAsync(u => u.Id == target.UnitId, ct);
        UnitScope.For(me, Feature, Cap.Delete).Require(unit);
        var why = Require.Reason(req.Reason);
        Remove(target, me, why, req.DecisionRef, clock.UtcNow);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.target_deleted", "حذف مستهدف مؤشر", "Deleted an indicator target") with
        {
            Target = new AuditTarget("indicator_target", target.Id.ToString(), $"{unit.Code}/{target.Year}", unit.Code),
            Reason = why,
            Details = new { target.Value, target.Text, target.Status },
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// يحذف المستهدف وتعديلاته. المعتمد يحتاج صلاحية الاعتماد وسببًا ورقم قرار؛ قيمته وقراره يبقيان في سجل التدقيق المتسلسل.
    /// </summary>
    private void Remove(IndicatorTarget target, EffectiveAccess me, string? reason, string? decisionRef, DateTimeOffset now)
    {
        if (target.Status == TargetStatus.Approved)
        {
            var (why, decision) = AmendApproved(me, reason, decisionRef);
            audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.approved_target_deleted", "حذف مستهدف معتمد بقرار", "Deleted an approved target") with
            {
                Target = new AuditTarget("indicator_target", target.Id.ToString(), $"{target.Year}"),
                Reason = why,
                Details = new { target.Year, target.Value, target.Text, decision, at = now },
            });
        }
        db.TargetRevisions.RemoveRange(db.TargetRevisions.Where(r => r.TargetId == target.Id));
        db.Targets.Remove(target);
    }

    private (string Reason, string Decision) AmendApproved(EffectiveAccess me, string? reason, string? decisionRef)
    {
        if (!me.HasSpecial(Feature, Specials.Approve)) throw DomainException.Forbidden();
        var why = Require.Reason(reason);
        var decision = Require.Optional(decisionRef, 200)
            ?? throw new DomainException("decision_ref_required", "تعديل مستهدف معتمد أو حذفه يحتاج رقم قرار اللجنة العليا.", "Changing an approved target needs a decision reference.", 400);
        return (why, decision);
    }

    /// <summary>تعديل معتمد: صلاحية الاعتماد وسبب ورقم قرار، والقيمة السابقة تحفظ.</summary>
    private void Revise(IndicatorTarget target, decimal? value, string? text, EffectiveAccess me, string? reason, string? decisionRef, DateTimeOffset now)
    {
        var (why, decision) = AmendApproved(me, reason, decisionRef);
        db.TargetRevisions.Add(new TargetRevision
        {
            TargetId = target.Id, OldValue = target.Value, OldText = target.Text, NewValue = value, NewText = text,
            Reason = why, DecisionRef = decision, ChangedBy = current.UserId!.Value, ChangedAt = now,
        });
        target.DecisionRef = decision;
    }

    /// <summary>اعتماد المستهدف بقرار؛ من أدخله آخر مرة لا يعتمده.</summary>
    public async Task<TargetDto> ApproveAsync(Guid id, CommitteeDecisionRequest req, CancellationToken ct)
    {
        await access.RequireSpecialAsync(Feature, Specials.Approve, ct);
        var target = await db.Targets.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound();
        if (target.Status == TargetStatus.Approved) throw new DomainException("already_approved", "المستهدف معتمد بالفعل.", "The target is already approved.");
        if (target.UpdatedBy == current.UserId)
            throw DomainException.SeparationOfDuties("SOD-K2", "من أدخل المستهدف لا يعتمده.", "Whoever entered the target cannot approve it.");
        target.Status = TargetStatus.Approved;
        target.ApprovedBy = current.UserId;
        target.ApprovedAt = clock.UtcNow;
        target.DecisionRef = Require.Optional(req.DecisionRef, 200)
            ?? throw new DomainException("decision_ref_required", "اكتب رقم محضر اللجنة العليا.", "Enter the committee minutes reference.", 400);
        var unit = await db.OrgUnits.AsNoTracking().FirstAsync(u => u.Id == target.UnitId, ct);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.target_approved", "اعتماد مستهدف مؤشر", "Approved an indicator target") with
        {
            Target = new AuditTarget("indicator_target", target.Id.ToString(), $"{target.Year}", unit.Code),
            Details = new { target.DecisionRef },
        });
        await db.SaveChangesAsync(ct);
        return ToDto(target, unit);
    }

    /// <summary>هل يحقق توزيع الكليات مستهدف الجامعة لهذه السنة؟ الكلية بلا مستهدف خاص يسري عليها الموحد «لكل كلية».</summary>
    public async Task<DistributionDto> DistributionAsync(string code, int year, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var (indicator, version) = await LoadIndicatorAsync(code, ct);
        var university = await UniversityAsync(ct);
        var colleges = await db.OrgUnits.AsNoTracking()
            .Where(u => u.Active && (u.Kind == OrgUnitKind.College || u.Kind == OrgUnitKind.Institute))
            .OrderBy(u => u.SortOrder).ThenBy(u => u.NameAr).ToListAsync(ct);
        var targets = await db.Targets.AsNoTracking().Where(t => t.IndicatorId == indicator.Id).ToListAsync(ct);
        var each = targets.FirstOrDefault(t => t.Kind == TargetKind.EachCollege);
        var uni = targets.FirstOrDefault(t => t.UnitId == university.Id && t.Year == year && t.Kind == TargetKind.Target);

        var shares = colleges.Select(c =>
        {
            var own = targets.FirstOrDefault(t => t.UnitId == c.Id && t.Year == year && t.Kind == TargetKind.Target);
            return own is not null
                ? new CollegeShare(c.Id, c.NameAr, own.Value, own.Numerator, own.Denominator, own.Text, false)
                : new CollegeShare(c.Id, c.NameAr, each?.Value, each?.Numerator, each?.Denominator, each?.Text, each is not null);
        }).ToList();

        var result = DistributionRules.Compare(version.Aggregation, version.Direction, version.Unit.Trim() == "%", uni?.Value, shares);
        return new DistributionDto(year, result, shares);
    }

    private async Task<(Indicator Indicator, IndicatorVersion Version)> LoadIndicatorAsync(string code, CancellationToken ct)
    {
        var indicator = await db.Indicators.AsNoTracking().Include(i => i.Versions).FirstOrDefaultAsync(i => i.Code == code.Trim().ToUpperInvariant(), ct)
            ?? throw DomainException.NotFound();
        return (indicator, indicator.Effective ?? indicator.Current);
    }

    private async Task<OrgUnit> UniversityAsync(CancellationToken ct) =>
        await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Kind == OrgUnitKind.University, ct)
        ?? throw new InvalidOperationException("The university root unit is missing.");

    private static TargetDto ToDto(IndicatorTarget t, OrgUnit u) =>
        new(t.Id, u.Id, u.Code, u.NameAr, t.Year, t.Kind, t.Value, t.Text, t.Numerator, t.Denominator, t.Source, t.Note, t.Status, t.DecisionRef, t.Version);
}

/// <summary>
/// التحقق من المستهدف قبل حفظه (دالة نقية): السنة في الخطة، والجهة المناسبة للنوع، ونطاق النسبة، والبسط والمقام.
/// يعيد القيمة التي تحفظ: إن أعطي البسط والمقام تحسب النسبة منهما في الخادم ولا تؤخذ من العميل، فلا تعدل النتيجة المحسوبة يدويًا.
/// </summary>
public static class TargetRules
{
    public static decimal? Validate(SaveTargetRequest req, OrgUnit unit, IndicatorVersion version)
    {
        if (req.Year is < TargetsService.PlanStart or > TargetsService.PlanEnd)
            throw Invalid("invalid_year", $"السنة من {TargetsService.PlanStart} إلى {TargetsService.PlanEnd}.");
        if (req.Kind == TargetKind.Baseline && req.Year != TargetsService.PlanStart)
            throw Invalid("baseline_year", $"خط الأساس لسنة {TargetsService.PlanStart} فقط.");
        if (req.Kind == TargetKind.EachCollege && unit.Kind != OrgUnitKind.University)
            throw Invalid("each_college_unit", "المستهدف الموحد لكل كلية يسجل على مستوى الجامعة.");
        if (req.Kind != TargetKind.EachCollege && unit.Kind is not (OrgUnitKind.University or OrgUnitKind.College or OrgUnitKind.Institute))
            throw Invalid("target_unit", "المستهدفات للجامعة أو لكلية أو معهد.");
        var percent = version.Unit.Trim() == "%";
        if ((req.Numerator is null) != (req.Denominator is null))
            throw Invalid("numerator_denominator", "أدخل البسط والمقام معًا، أو اتركهما معًا.");
        if (req.Numerator is { } n && req.Denominator is { } d)
        {
            if (n < 0 || d <= 0)
                throw Invalid("numerator_denominator", "البسط لا يكون سالبًا، والمقام أكبر من صفر.");
            if (percent && n > d)
                throw Invalid("numerator_exceeds", $"البسط {DistributionRules.Fmt(n)} أكبر من المقام {DistributionRules.Fmt(d)}؛ راجع إحدى القيمتين.");
            return decimal.Round(n / d * (percent ? 100 : 1), 4);
        }

        if (req.Value is null && string.IsNullOrWhiteSpace(req.Text))
            throw Invalid("value_required", "اكتب القيمة المستهدفة، أو وصفها إن لم يحسم رقمها بعد.");
        if (percent && req.Value is { } p && (p < 0 || p > 100))
            throw Invalid("percent_range", $"النسبة من 0 إلى 100؛ القيمة المدخلة {DistributionRules.Fmt(p)}.");
        if (!percent && req.Value is < 0)
            throw Invalid("negative_value", "القيمة لا تكون سالبة لهذا المؤشر.");
        return req.Value;
    }

    private static DomainException Invalid(string code, string messageAr) => new(code, messageAr, messageAr, 400);
}
