using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Collection;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Scorecard;

public sealed record ReviewCycle(Guid Id, string Name, int Year, int Quarter, string Label, CycleStatus Status);

/// <summary>حالة مؤشر الجامعة في الربع.</summary>
public enum ReviewState
{
    /// <summary>لا قيمة معتمدة من أي جهة بعد.</summary>
    NoData = 0,

    /// <summary>بعض الجهات فقط اعتمدت قيمها.</summary>
    Incomplete = 1,

    /// <summary>القيمة المحسوبة جاهزة ولم تنقل بعد.</summary>
    Ready = 2,

    /// <summary>نقلت إلى مؤشر الجامعة وما زالت تطابق المحسوبة.</summary>
    Transferred = 3,

    /// <summary>نقلت ثم تغيرت قيم الجهات (إعادة فتح نموذج مثلًا) فاختلفت المحسوبة عن المنقولة.</summary>
    Changed = 4,
}

/// <summary>
/// قيمة جهة في المؤشر: ببسطها ومقامها وحالة نموذجها، وإنجازها من مستهدفها ومن مستهدف الجامعة وبعدها عنه (للنسب والمتوسطات فقط،
/// إذ لا يقارن عدد كلية بعدد الجامعة كلها)، وحصتها من قيمة الجامعة المحسوبة (للمجموع والعدد).
/// </summary>
public sealed record ReviewUnit(
    Guid UnitId, string Name, OrgUnitKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, int Reported, int Expected, string? Note,
    SubmissionStatus? FormStatus, decimal? Target, Kpi.TargetSource TargetSource, decimal? Achievement, decimal? UniversityAchievement, decimal? GapToUniversity,
    decimal? Share);

public sealed record OfficialValue(
    decimal? Value, decimal? Numerator, decimal? Denominator, decimal? ComputedValue, string? Note, string? TransferredByName, DateTimeOffset TransferredAt, uint Version);

/// <summary>تغيير في القيمة الرسمية للجامعة (D8): السابقة والجديدة والمحسوبة، وكم جهة أبلغت، والسبب، ومن ومتى.</summary>
public sealed record ValueChangeDto(decimal? OldValue, decimal? NewValue, decimal? ComputedValue, int Reported, int Expected, string? Reason, string? ChangedByName, DateTimeOffset ChangedAt);

public sealed record ReviewRow(
    Guid IndicatorId, string Code, string Name, string StandardCode, string? PillarCode, int PillarIndex, string Unit, Direction Direction, Aggregation Aggregation,
    CollectionLevel Level, string? NumeratorLabel, string? DenominatorLabel, string? Formula,
    decimal? Computed, decimal? ComputedNumerator, decimal? ComputedDenominator, int Reported, int Expected, string? Note,
    decimal? UniversityTarget, decimal? ComputedAchievement, OfficialValue? Official, ReviewState State, bool Comparable, IReadOnlyList<ReviewUnit> Units,
    IReadOnlyList<ValueChangeDto>? History = null);

public sealed record ReviewDto(IReadOnlyList<ReviewCycle> Cycles, ReviewCycle? Cycle, IReadOnlyList<ReviewRow> Rows, bool CanTransfer);

public sealed record TransferRequest(Guid CycleId, Guid IndicatorId, decimal? Value, string? Note, uint? Version);

public sealed record TransferAllRequest(Guid CycleId);

/// <summary>
/// مراجعة مسئول ملف Tier 1: كل مؤشر في الربع بقيمة كل كلية وحساب الجامعة منها، ثم نقل القيمة إلى مؤشر الجامعة.
/// القيمة المنقولة وحدها تدخل بطاقة الأداء والفئة وملف المجلس. تعديلها عن المحسوبة، أو نقلها قبل اكتمال الجهات، بسبب مكتوب.
/// </summary>
public sealed class ReviewService(IScorecardStore db, IReadModel read, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, ResultsService results)
{
    private const string Feature = Features.University;

    public async Task<ReviewDto> GetAsync(Guid? cycleId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var cycles = await QuarterlyCyclesAsync(ct);
        var cycle = cycleId is { } id ? cycles.FirstOrDefault(c => c.Id == id) ?? throw DomainException.NotFound()
            // الافتراضي: أحدث ربع فيه قيم معتمدة لم تنقل، وإلا أحدث ربع.
            : cycles.FirstOrDefault();
        if (cycle is null) return new ReviewDto([], null, [], me.Has(Feature, Cap.Edit));
        return new ReviewDto(cycles.Select(Dto).ToList(), Dto(cycle), await RowsAsync(cycle, ct), me.Has(Feature, Cap.Edit));
    }

    public async Task<ReviewRow> TransferAsync(TransferRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var cycle = (await QuarterlyCyclesAsync(ct)).FirstOrDefault(c => c.Id == req.CycleId) ?? throw DomainException.NotFound();
        var row = (await RowsAsync(cycle, ct)).FirstOrDefault(r => r.IndicatorId == req.IndicatorId) ?? throw DomainException.NotFound();
        await SaveAsync(cycle, row, req.Value, req.Note, req.Version, ct);
        await db.SaveChangesAsync(ct);
        return (await RowsAsync(cycle, ct)).First(r => r.IndicatorId == req.IndicatorId);
    }

    /// <summary>ينقل كل مؤشر اكتملت جهاته وقيمته المحسوبة جاهزة أو تغيرت، كما حسبت دون تعديل.</summary>
    public async Task<int> TransferAllAsync(TransferAllRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var cycle = (await QuarterlyCyclesAsync(ct)).FirstOrDefault(c => c.Id == req.CycleId) ?? throw DomainException.NotFound();
        var ready = (await RowsAsync(cycle, ct)).Where(r => r.State is ReviewState.Ready or ReviewState.Changed && r.Reported >= r.Expected && r.Computed is not null).ToList();
        foreach (var row in ready) await SaveAsync(cycle, row, null, null, null, ct);
        await db.SaveChangesAsync(ct);
        return ready.Count;
    }

    /// <summary>DB-6: the precision of every value column (numeric(20,4)), rounded half away from zero.</summary>
    internal static decimal? Round4(decimal? x) => x is { } v ? decimal.Round(v, 4, MidpointRounding.AwayFromZero) : null;

    private async Task SaveAsync(CollectionCycle cycle, ReviewRow row, decimal? value, string? note, uint? version, CancellationToken ct)
    {
        var final = value ?? row.Computed ?? throw new DomainException("no_value", "لا قيمة محسوبة لهذا المؤشر بعد؛ اكتب القيمة بسبب.", "There is no computed value; enter one with a reason.", 400) { Field = "value" };
        var adjusted = row.Computed is not { } c || c != final;
        var partial = row.Reported < row.Expected;
        var why = Require.Optional(note, 1000);
        if ((adjusted || partial) && (why is null || why.Length < 5))
            throw new DomainException("note_required", partial
                ? $"اعتمدت قيم {row.Reported} من {row.Expected} جهة فقط؛ اكتب سبب النقل قبل اكتمالها."
                : "القيمة تختلف عن المحسوبة من الكليات؛ اكتب سبب التعديل.", "A reason is required.", 400) { Field = "note" };

        var existing = await db.UniversityValues.FirstOrDefaultAsync(v => v.IndicatorId == row.IndicatorId && v.Year == cycle.TargetYear && v.Quarter == cycle.Quarter, ct);
        if (existing is not null) Concurrency.ExpectUpsert(db, existing, version);
        var old = existing?.Value;
        var entity = existing ?? new UniversityValue { IndicatorId = row.IndicatorId, Year = cycle.TargetYear, Quarter = cycle.Quarter!.Value };
        entity.CycleId = cycle.Id;
        entity.Value = Round4(final);
        entity.Numerator = adjusted ? null : Round4(row.ComputedNumerator);
        entity.Denominator = adjusted ? null : Round4(row.ComputedDenominator);
        entity.ComputedValue = Round4(row.Computed);
        entity.Reported = row.Reported;
        entity.Expected = row.Expected;
        entity.Note = why;
        entity.TransferredBy = current.UserId!.Value;
        entity.TransferredByName = current.DisplayName;
        entity.TransferredAt = clock.UtcNow;
        if (existing is null) db.UniversityValues.Add(entity);
        // D8: كل نقل يحفظ في التاريخ بالقيمة السابقة والجديدة، فلا تمحى القيمة الرسمية السابقة بالكتابة فوقها.
        db.UniversityValueChanges.Add(new UniversityValueChange
        {
            IndicatorId = row.IndicatorId, Year = cycle.TargetYear, Quarter = cycle.Quarter!.Value, CycleId = cycle.Id,
            OldValue = old, NewValue = final, ComputedValue = row.Computed, Reported = row.Reported, Expected = row.Expected, Reason = why,
            ChangedBy = entity.TransferredBy, ChangedByName = entity.TransferredByName, ChangedAt = entity.TransferredAt,
        });

        audit.Record(AuditEvents.Audit("dc", Feature, "dc.university_value_transferred", "نقل قيمة إلى مؤشر الجامعة", "Transferred a value to the university indicator") with
        {
            Target = new AuditTarget("indicator", row.IndicatorId.ToString(), row.Code, PlanCalendar.Label(cycle.TargetYear, cycle.Quarter!.Value)),
            Changes = [new FieldChange("value", old?.ToString(System.Globalization.CultureInfo.InvariantCulture), final.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            Reason = why,
            Details = new { computed = row.Computed, row.Reported, row.Expected, adjusted },
        });
    }

    private async Task<List<CollectionCycle>> QuarterlyCyclesAsync(CancellationToken ct) =>
        await read.Query<Domain.Collection.CollectionCycle>().AsNoTracking().Where(c => c.Status != CycleStatus.Draft && !c.Trial && c.Quarter != null)
            .OrderByDescending(c => c.TargetYear).ThenByDescending(c => c.Quarter).ToListAsync(ct);

    private static ReviewCycle Dto(CollectionCycle c) => new(c.Id, c.Name, c.TargetYear, c.Quarter!.Value, PlanCalendar.Label(c.TargetYear, c.Quarter!.Value), c.Status);

    private async Task<List<ReviewRow>> RowsAsync(CollectionCycle cycle, CancellationToken ct)
    {
        var breakdown = await results.BreakdownAsync(cycle, ct);
        var official = await db.UniversityValues.AsNoTracking().Where(v => v.Year == cycle.TargetYear && v.Quarter == cycle.Quarter).ToDictionaryAsync(v => v.IndicatorId, ct);
        var history = (await db.UniversityValueChanges.AsNoTracking().Where(h => h.Year == cycle.TargetYear && h.Quarter == cycle.Quarter)
                .OrderByDescending(h => h.ChangedAt).ToListAsync(ct))
            .ToLookup(h => h.IndicatorId, h => new ValueChangeDto(h.OldValue, h.NewValue, h.ComputedValue, h.Reported, h.Expected, h.Reason, h.ChangedByName, h.ChangedAt));
        var pillars = await read.Query<Pillar>().AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        var standards = await read.Query<Standard>().AsNoTracking().ToDictionaryAsync(s => s.Code, ct);

        return breakdown.Select(b =>
        {
            var v = b.Version;
            var pillarId = standards.GetValueOrDefault(b.StandardCode)?.PillarId;
            var pillarIndex = pillars.FindIndex(p => p.Id == pillarId);
            var o = official.GetValueOrDefault(b.IndicatorId);
            // DB-6: official values are stored at 4 decimals; compare and store the computed value at the same precision.
            var computed = Round4(b.University.Value);
            var state = o is not null ? (o.ComputedValue == computed ? ReviewState.Transferred : ReviewState.Changed)
                : computed is null ? (b.University.Reported == 0 ? ReviewState.NoData : ReviewState.Incomplete)
                : b.University.Reported < b.University.Expected ? ReviewState.Incomplete
                : ReviewState.Ready;
            // كلية مقابل مستهدف الجامعة للنسب والمتوسطات فقط؛ مركزي على مستوى الجامعة يقارن بمستهدفها دائمًا (رقمه رقم الجامعة).
            var comparable = Kpi.TargetLookup.ComparableAcrossLevels(v.Aggregation) || b.Level == CollectionLevel.University;
            var units = b.Units.Select(u => new ReviewUnit(u.UnitId, u.UnitName, u.Kind, u.Value, u.Numerator, u.Denominator, u.Reported, u.Expected, u.Note,
                u.FormStatus, u.Target, u.TargetSource,
                ScorecardRules.Achievement(u.Value, u.Target, v.Direction),
                comparable ? ScorecardRules.Achievement(u.Value, b.UniversityTarget, v.Direction) : null,
                comparable && u.Value is { } x && b.UniversityTarget is { } t ? x - t : null,
                !comparable && u.Value is { } s && computed is { } all && all != 0 ? Math.Round(s / all * 100, 1) : null)).ToList();
            return new ReviewRow(b.IndicatorId, b.Code, v.NameAr, b.StandardCode, pillarIndex >= 0 ? pillars[pillarIndex].Code : null, pillarIndex, v.Unit, v.Direction,
                v.Aggregation, b.Level, v.Numerator, v.Denominator, v.Formula,
                computed, b.University.Numerator, b.University.Denominator, b.University.Reported, b.University.Expected, b.University.Note,
                b.UniversityTarget, ScorecardRules.Achievement(computed, b.UniversityTarget, v.Direction),
                o is null ? null : new OfficialValue(o.Value, o.Numerator, o.Denominator, o.ComputedValue, o.Note, o.TransferredByName, o.TransferredAt, o.Version),
                state, comparable, units, [.. history[b.IndicatorId]]);
        }).ToList();
    }
}
