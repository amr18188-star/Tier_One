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

public sealed record ResultUnit(Guid Id, string Name, OrgUnitKind Kind);

/// <summary>
/// نتيجة مؤشر لجهة. للكلية في مؤشر نسبة أو متوسط: مستهدف الجامعة ونسبة الإنجاز منه أيضًا، فتعرف بعدها عن نسبة الجامعة المرجوة.
/// </summary>
public sealed record IndicatorResult(
    Guid IndicatorId, string Code, string Name, string StandardCode, string Unit, Direction Direction, CollectionLevel Level,
    decimal? Actual, decimal? Target, decimal? Baseline, decimal? Previous, decimal? Achievement, Performance Performance,
    int Reported, int Expected, string? Note, string Summary,
    Kpi.TargetSource TargetSource, int? TargetYear, decimal? UniversityTarget, decimal? UniversityAchievement);

public sealed record StandardResult(string Code, string Name, int Indicators, int Achieved, int Near, int Behind, int NoData, int NoTarget);

public sealed record HeatCell(Guid UnitId, string StandardCode, int Indicators, int Achieved, int Near, int Behind, int NoData);

public sealed record ResultsCycle(Guid Id, string Name, string PeriodLabel, int TargetYear, CycleStatus Status, bool Trial);

public sealed record ResultsDto(
    ResultsCycle Cycle, ResultUnit Unit, IReadOnlyList<ResultUnit> Units, ResultsCycle? PreviousCycle,
    int FormsApproved, int FormsTotal, IReadOnlyList<StandardResult> Standards, IReadOnlyList<IndicatorResult> Indicators,
    IReadOnlyList<ResultUnit> HeatUnits, IReadOnlyList<HeatCell> Heat, bool CanExport);

public sealed record UnitResult(
    Guid UnitId, string UnitName, decimal? Actual, decimal? Target, decimal? Achievement, Performance Performance, int Reported, int Expected, string? Note,
    Kpi.TargetSource TargetSource, decimal? Numerator, decimal? Denominator);

public sealed record TrendPoint(Guid CycleId, string Label, decimal? Actual, decimal? Target);

/// <summary>قيمة جهة في مؤشر ضمن دورة: ببسطها ومقامها، وكم جهة تحتها أبلغت، وحالة نموذجها، ومستهدفها.</summary>
public sealed record UnitBreakdown(
    Guid UnitId, string UnitName, OrgUnitKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, int Reported, int Expected, string? Note,
    SubmissionStatus? FormStatus, decimal? Target, Kpi.TargetSource TargetSource);

/// <summary>مؤشر في دورة: قيمة الجامعة المحسوبة من الجهات ومستهدفها، وقيمة كل جهة.</summary>
public sealed record IndicatorBreakdown(
    Guid IndicatorId, string Code, IndicatorVersion Version, string StandardCode, CollectionLevel Level, Aggregate University, decimal? UniversityTarget,
    IReadOnlyList<UnitBreakdown> Units);

public sealed record IndicatorResultsDto(IndicatorResult University, IReadOnlyList<UnitResult> Colleges, IReadOnlyList<TrendPoint> Trend);

/// <summary>
/// النتائج من القيم المعتمدة نهائيًا فقط: قيمة الكلية من أقسامها أو من نموذجها، وقيمة الجامعة من كلياتها أو من الجهة المركزية،
/// بقاعدة تجميع المؤشر. من نطاقه الجامعة يرى الجامعة وكل الكليات؛ ومن نطاقه كليات يرى كلياته فقط.
/// </summary>
public sealed class ResultsService(IAppDb db, IAccessService access, IAuditLog audit, Settings.SettingsService settings)
{
    private const string Feature = Features.Results;

    public async Task<ResultsDto?> OverviewAsync(Guid? cycleId, Guid? unitId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var scope = UnitScope.For(me, Feature, Cap.View);
        var cycle = await PickCycleAsync(cycleId, ct);
        if (cycle is null) return null;
        var snap = await SnapshotAsync(cycle, ct);
        var units = VisibleUnits(snap, scope);
        if (units.Count == 0) throw DomainException.NotFound();
        var unit = unitId is { } u ? units.FirstOrDefault(x => x.Id == u) ?? throw DomainException.NotFound() : units[0];
        var org = snap.Units[unit.Id];

        var previousCycle = await PreviousCycleAsync(cycle, ct);
        var previous = previousCycle is null ? null : await SnapshotAsync(previousCycle, ct);
        var near = await NearAsync(ct);

        var indicators = snap.Indicators.Where(i => org.Kind == OrgUnitKind.University || i.Ci.Level != CollectionLevel.University)
            .Select(i => Result(snap, i, org, previous, near)).ToList();
        var standards = snap.Standards.Values.OrderBy(s => s.SortOrder)
            .Select(s =>
            {
                var rows = indicators.Where(r => r.StandardCode == s.Code).ToList();
                return new StandardResult(s.Code, s.NameAr, rows.Count, rows.Count(r => r.Performance == Performance.Achieved), rows.Count(r => r.Performance == Performance.Near),
                    rows.Count(r => r.Performance == Performance.Behind), rows.Count(r => r.Performance == Performance.NoData), rows.Count(r => r.Performance == Performance.NoTarget));
            })
            .Where(s => s.Indicators > 0).ToList();

        // خريطة الكليات × المعايير: من نطاقه الجامعة يرى كل الكليات، وإلا كلياته.
        var heatUnits = units.Where(x => x.Kind is OrgUnitKind.College or OrgUnitKind.Institute).ToList();
        var heat = new List<HeatCell>();
        foreach (var college in heatUnits)
        {
            var cu = snap.Units[college.Id];
            foreach (var group in snap.Indicators.Where(i => i.Ci.Level != CollectionLevel.University).GroupBy(i => i.Standard.Code))
            {
                var perf = group.Select(i => Judge(snap, i, cu, near)).ToList();
                heat.Add(new HeatCell(college.Id, group.Key, perf.Count, perf.Count(p => p == Performance.Achieved), perf.Count(p => p == Performance.Near),
                    perf.Count(p => p == Performance.Behind), perf.Count(p => p == Performance.NoData)));
            }
        }

        var formsInScope = snap.Submissions.Where(s => org.Kind == OrgUnitKind.University || snap.Units[s.UnitId].Path.StartsWith(org.Path, StringComparison.Ordinal)).ToList();
        return new ResultsDto(Cycle(cycle), unit, units, previousCycle is null ? null : Cycle(previousCycle),
            formsInScope.Count(s => s.Status == SubmissionStatus.Approved), formsInScope.Count, standards, indicators, heatUnits, heat,
            me.HasSpecial(Feature, "export"));
    }

    public async Task<IndicatorResultsDto> IndicatorAsync(Guid cycleId, Guid indicatorId, Guid? unitId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var scope = UnitScope.For(me, Feature, Cap.View);
        var cycle = await db.Cycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cycleId && c.Status != CycleStatus.Draft, ct) ?? throw DomainException.NotFound();
        var snap = await SnapshotAsync(cycle, ct);
        var units = VisibleUnits(snap, scope);
        var unit = unitId is { } u ? units.FirstOrDefault(x => x.Id == u) ?? throw DomainException.NotFound() : units.FirstOrDefault() ?? throw DomainException.NotFound();
        var ind = snap.Indicators.FirstOrDefault(i => i.Ci.IndicatorId == indicatorId) ?? throw DomainException.NotFound();
        var near = await NearAsync(ct);
        var previousCycle = await PreviousCycleAsync(cycle, ct);
        var previous = previousCycle is null ? null : await SnapshotAsync(previousCycle, ct);
        var main = Result(snap, ind, snap.Units[unit.Id], previous, near);

        var colleges = ind.Ci.Level == CollectionLevel.University ? [] : units.Where(x => x.Kind is OrgUnitKind.College or OrgUnitKind.Institute)
            .Select(c =>
            {
                var cu = snap.Units[c.Id];
                var agg = Actual(snap, ind, cu);
                var target = TargetFor(snap, ind, cu);
                return new UnitResult(c.Id, c.Name, agg.Value, target.Value, ResultsRules.Achievement(ind.Version.Direction, agg.Value, target.Value),
                    ResultsRules.Status(ind.Version.Aggregation, ind.Version.Direction, agg, target.Value, target.NotStarted, near), agg.Reported, agg.Expected, agg.Note,
                    target.Source, agg.Numerator, agg.Denominator);
            })
            .OrderByDescending(r => r.Achievement ?? -1).ToList();

        // الاتجاه عبر الدورات المفتوحة والمغلقة بترتيبها الزمني (آخر ثماني دورات).
        // الدورات التجريبية لا تختلط بالرسمية؛ ودورتان بتاريخ فتح واحد ترتبان بوقت إنشائهما.
        var cycles = await db.Cycles.AsNoTracking()
            .Where(c => c.Status != CycleStatus.Draft && c.Trial == cycle.Trial
                        && (c.OpensOn < cycle.OpensOn || (c.OpensOn == cycle.OpensOn && c.CreatedAt <= cycle.CreatedAt)))
            .OrderByDescending(c => c.OpensOn).ThenByDescending(c => c.CreatedAt).Take(8).ToListAsync(ct);
        var trend = new List<TrendPoint>();
        foreach (var c in cycles.OrderBy(c => c.OpensOn).ThenBy(c => c.CreatedAt))
        {
            var s = c.Id == cycle.Id ? snap : await SnapshotAsync(c, ct);
            var i = s.Indicators.FirstOrDefault(x => x.Ci.IndicatorId == indicatorId);
            if (i is null || !s.Units.TryGetValue(unit.Id, out var tu)) continue;
            trend.Add(new TrendPoint(c.Id, c.PeriodLabel, Actual(s, i, tu).Value, TargetFor(s, i, tu).Value));
        }
        return new IndicatorResultsDto(main, colleges, trend);
    }

    /// <summary>كشف Excel بالنتائج: صف لكل مؤشر في الجهة المختارة ثم عمود لكل كلية مرئية.</summary>
    public async Task<(byte[] Content, string FileName)> ExportAsync(Guid cycleId, Guid? unitId, ISpreadsheets sheets, CancellationToken ct)
    {
        await access.RequireSpecialAsync(Feature, "export", ct);
        var overview = await OverviewAsync(cycleId, unitId, ct) ?? throw DomainException.NotFound();
        var cycle = await db.Cycles.AsNoTracking().FirstAsync(c => c.Id == cycleId, ct);
        var snap = await SnapshotAsync(cycle, ct);
        var colleges = overview.HeatUnits;
        var headers = new List<string> { "الرمز", "المؤشر", "المعيار", "الوحدة", "الفعلي", "المستهدف", "خط الأساس", "الدورة السابقة", "نسبة الإنجاز", "الحالة", "أبلغ/المتوقع", "ملاحظة" };
        headers.AddRange(colleges.Select(c => c.Name));
        var rows = overview.Indicators.Select(r =>
        {
            var row = new List<string?>
            {
                r.Code, r.Name, r.StandardCode, r.Unit, Fmt(r.Actual), Fmt(r.Target), Fmt(r.Baseline), Fmt(r.Previous),
                r.Achievement is { } a ? $"{Math.Round(a * 100)}%" : null, PerformanceText(r.Performance), $"{r.Reported}/{r.Expected}", r.Note,
            };
            var ind = snap.Indicators.First(i => i.Ci.IndicatorId == r.IndicatorId);
            row.AddRange(colleges.Select(c => ind.Ci.Level == CollectionLevel.University ? null : Fmt(Actual(snap, ind, snap.Units[c.Id]).Value)));
            return (IReadOnlyList<string?>)row;
        }).ToList();
        var bytes = sheets.Write(new Sheet("النتائج", headers, rows));
        await audit.RecordNowAsync(AuditEvents.Access("dc", Feature, "dc.results_exported", "تصدير نتائج دورة", "Exported cycle results") with
        {
            Target = new AuditTarget("cycle", cycle.Id.ToString(), cycle.Name, overview.Unit.Name),
        }, ct);
        return (bytes, $"results-{cycle.PeriodLabel}.xlsx");
    }

    /// <summary>
    /// تفصيل كل مؤشر في دورة: قيمة كل كلية (أو الجهة المركزية) من قيمها المعتمدة ببسطها ومقامها وحالة نموذجها ومستهدفها،
    /// وقيمة الجامعة المحسوبة منها بقاعدة التجميع ومستهدف الجامعة لسنة الدورة. لمراجعة مسئول ملف Tier 1 قبل نقل القيمة.
    /// </summary>
    public async Task<IReadOnlyList<IndicatorBreakdown>> BreakdownAsync(CollectionCycle cycle, CancellationToken ct)
    {
        var snap = await SnapshotAsync(cycle, ct);
        var formOf = snap.Submissions.ToDictionary(s => s.UnitId, s => s.Status);
        var colleges = snap.Units.Values.Where(u => u.IsCollegeLevel && u.Active).OrderBy(u => u.SortOrder).ThenBy(u => u.NameAr).ToList();
        var list = new List<IndicatorBreakdown>();
        foreach (var i in snap.Indicators)
        {
            var uni = Actual(snap, i, snap.Root);
            var uniTarget = Kpi.TargetLookup.University([.. snap.Targets[i.Ci.IndicatorId]], snap.Root.Id, cycle.TargetYear, snap.PlanStart.GetValueOrDefault(i.Ci.IndicatorId));
            IEnumerable<OrgUnit> contributors = i.Ci.Level == CollectionLevel.University
                ? [snap.Units[i.Ci.CentralUnitId ?? snap.Root.Id]]
                // الكليات المكلفة به (هي أو أقسامها) في هذه الدورة فقط.
                : colleges.Where(c => snap.Submissions.Any(s => snap.Units[s.UnitId] is var su && su.Path.StartsWith(c.Path, StringComparison.Ordinal)
                                                                && (su.Kind == OrgUnitKind.Department || su.IsCollegeLevel) && FormsService.Applies(i.Ci, su)));
            var units = contributors.Select(u =>
            {
                var a = u.Kind == OrgUnitKind.University ? uni : Actual(snap, i, u);
                var t = TargetFor(snap, i, u);
                return new UnitBreakdown(u.Id, u.NameAr, u.Kind, a.Value, a.Numerator, a.Denominator, a.Reported, a.Expected, a.Note,
                    formOf.TryGetValue(u.Id, out var st) ? st : null, t.Value, t.Source);
            }).ToList();
            list.Add(new IndicatorBreakdown(i.Ci.IndicatorId, i.Ci.Code, i.Version, i.Standard.Code, i.Ci.Level, uni, uniTarget, units));
        }
        return list;
    }

    /// <summary>
    /// قيم جهة (كلية) في كل ربع من الدورات الربع سنوية الرسمية: (السنة، الربع) ← (المؤشر ← القيمة ببسطها ومقامها)،
    /// للمؤشرات التي تجمع منها (لا مؤشرات الجامعة المركزية).
    /// </summary>
    public async Task<Dictionary<(int Year, int Quarter), Dictionary<Guid, Aggregate>>> UnitQuartersAsync(Guid unitId, CancellationToken ct)
    {
        var cycles = await db.Cycles.AsNoTracking()
            .Where(c => c.Status != CycleStatus.Draft && !c.Trial && c.Quarter != null)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var result = new Dictionary<(int, int), Dictionary<Guid, Aggregate>>();
        foreach (var c in cycles)
        {
            var snap = await SnapshotAsync(c, ct);
            if (!snap.Units.TryGetValue(unitId, out var unit)) continue;
            result[(c.TargetYear, c.Quarter!.Value)] = snap.Indicators.Where(i => i.Ci.Level != CollectionLevel.University)
                .ToDictionary(i => i.Ci.IndicatorId, i => Actual(snap, i, unit));
        }
        return result;
    }

    public static string PerformanceText(Performance p) => p switch
    {
        Performance.Achieved => "محقق",
        Performance.Near => "قريب",
        Performance.Behind => "متأخر",
        Performance.NoTarget => "بلا مستهدف",
        Performance.NotJudged => "لا يحكم",
        Performance.NotStarted => "لم يبدأ مستهدفه",
        Performance.Incomplete => "بيانات جزئية",
        _ => "لا بيانات",
    };

    // ----------------------------------------------------------------- الحساب

    private sealed record Ind(CycleIndicator Ci, IndicatorVersion Version, Standard Standard, int SortOrder);

    private sealed class Snapshot
    {
        public required CollectionCycle Cycle { get; init; }
        public required List<Ind> Indicators { get; init; }
        public required Dictionary<Guid, OrgUnit> Units { get; init; }
        public required Dictionary<Guid, Standard> Standards { get; init; }
        public required List<Submission> Submissions { get; init; }

        /// <summary>القيم المعتمدة: (الجهة، المؤشر) ← القيمة.</summary>
        public required Dictionary<(Guid Unit, Guid Indicator), SubmittedValue> Approved { get; init; }

        /// <summary>المستهدفات حتى سنة مقارنة الدورة (آخر مستهدف محدد يستمر بعد سنته)، وخطوط الأساس.</summary>
        public required ILookup<Guid, IndicatorTarget> Targets { get; init; }

        /// <summary>سنة بدء مستهدف كل مؤشر في الخطة، إن حددت.</summary>
        public required Dictionary<Guid, int?> PlanStart { get; init; }

        public required OrgUnit Root { get; init; }
    }

    private async Task<Snapshot> SnapshotAsync(CollectionCycle cycle, CancellationToken ct)
    {
        var cis = await db.CycleIndicators.AsNoTracking().Where(c => c.CycleId == cycle.Id).ToListAsync(ct);
        var versionIds = cis.Select(c => c.VersionId).ToList();
        var versions = await db.IndicatorVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var indicatorIds = cis.Select(c => c.IndicatorId).ToList();
        var indicators = await db.Indicators.AsNoTracking().Where(i => indicatorIds.Contains(i.Id)).Select(i => new { i.Id, i.StandardId, i.SortOrder, i.PlanStartYear }).ToDictionaryAsync(i => i.Id, ct);
        var standards = await db.Standards.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var subs = await db.Submissions.AsNoTracking().Where(s => s.CycleId == cycle.Id).ToListAsync(ct);
        var approvedIds = subs.Where(s => s.Status == SubmissionStatus.Approved).Select(s => s.Id).ToList();
        var unitOf = subs.ToDictionary(s => s.Id, s => s.UnitId);
        var values = await db.SubmittedValues.AsNoTracking().Where(v => approvedIds.Contains(v.SubmissionId)).ToListAsync(ct);
        var targets = await db.Targets.AsNoTracking().Where(t => indicatorIds.Contains(t.IndicatorId) && (t.Year <= cycle.TargetYear || t.Kind == TargetKind.Baseline)).ToListAsync(ct);
        return new Snapshot
        {
            Cycle = cycle,
            Indicators = cis.Where(c => versions.ContainsKey(c.VersionId) && indicators.ContainsKey(c.IndicatorId))
                .Select(c => new Ind(c, versions[c.VersionId], standards[indicators[c.IndicatorId].StandardId], indicators[c.IndicatorId].SortOrder))
                .OrderBy(i => i.Standard.SortOrder).ThenBy(i => i.SortOrder).ToList(),
            Units = units,
            Standards = standards.Values.ToDictionary(s => s.Id),
            Submissions = subs,
            Approved = values.ToDictionary(v => (unitOf[v.SubmissionId], v.IndicatorId)),
            Targets = targets.ToLookup(t => t.IndicatorId),
            PlanStart = indicators.Values.ToDictionary(i => i.Id, i => i.PlanStartYear),
            Root = units.Values.Where(u => u.ParentId == null).OrderBy(u => u.SortOrder).First(),
        };
    }

    /// <summary>القيمة الفعلية لجهة: الجامعة من كلياتها أو أقسامها أو الجهة المركزية، والكلية من أقسامها أو نموذجها.</summary>
    private static Aggregate Actual(Snapshot s, Ind i, OrgUnit unit)
    {
        var spec = CycleSpec.Of(i.Version);
        var percent = ValueRules.IsPercent(spec);
        var formUnits = s.Submissions.Select(x => x.UnitId).ToHashSet();

        if (i.Ci.Level == CollectionLevel.University)
        {
            if (unit.Kind != OrgUnitKind.University) return new(null, null, null, 0, 0, 0, null);
            var central = i.Ci.CentralUnitId ?? s.Root.Id;
            return ResultsRules.Combine(i.Version.Aggregation == Aggregation.Sum ? Aggregation.Sum : Aggregation.Central, percent, Values(s, i, [central]), 1);
        }

        // العدد المميز لا يجمع (بحث مشترك بين كليتين يعد مرة واحدة): قيمة الجامعة من إدخالها المركزي بعد إزالة التكرار،
        // وقيمة الكلية من إدخالها هي، لا مجموع أقسامها.
        if (i.Version.Aggregation == Aggregation.DistinctCount)
        {
            Guid? own = unit.Kind == OrgUnitKind.University ? i.Ci.CentralUnitId
                : unit.IsCollegeLevel && (i.Ci.Level == CollectionLevel.College || i.Ci.CollegeEntry) ? unit.Id : null;
            if (own is { } id && formUnits.Contains(id))
            {
                var mine = Values(s, i, [id]);
                return mine.Count == 0 ? new(null, null, null, 0, 1, 0, null)
                    : new(mine[0].Kind == ValueKind.Number ? mine[0].Value : null, null, null, 1, 1, mine[0].Kind == ValueKind.NotApplicable ? 1 : 0, null);
            }
        }

        var level = i.Ci.Level == CollectionLevel.Department ? OrgUnitKind.Department : OrgUnitKind.College;
        var contributors = s.Units.Values
            .Where(u => formUnits.Contains(u.Id) && u.Path.StartsWith(unit.Path, StringComparison.Ordinal)
                        && (level == OrgUnitKind.Department ? u.Kind == OrgUnitKind.Department : u.IsCollegeLevel)
                        && FormsService.Applies(i.Ci, u))
            .Select(u => u.Id).ToList();
        var values = Values(s, i, contributors);
        // قيمة جهة واحدة (كلية لمؤشر على مستوى الكلية) تؤخذ كما هي.
        if (contributors.Count == 1 && unit.Id == contributors[0])
            return values.Count == 0 ? new(null, null, null, 0, 1, 0, null)
                : new(values[0].Kind == ValueKind.Number ? values[0].Value : null, values[0].Numerator, values[0].Denominator, 1, 1, values[0].Kind == ValueKind.NotApplicable ? 1 : 0, null);
        return ResultsRules.Combine(i.Version.Aggregation, percent, values, contributors.Count);
    }

    private static List<UnitValue> Values(Snapshot s, Ind i, IEnumerable<Guid> units) =>
        units.Select(u => s.Approved.GetValueOrDefault((u, i.Ci.IndicatorId))).Where(v => v is not null)
            .Select(v => new UnitValue(Guid.Empty, v!.Kind, v.Value, v.Numerator, v.Denominator)).ToList();

    private static Kpi.TargetInfo TargetFor(Snapshot s, Ind i, OrgUnit unit) =>
        Kpi.TargetLookup.For([.. s.Targets[i.Ci.IndicatorId]], unit, s.Root.Id, s.Cycle.TargetYear, s.PlanStart.GetValueOrDefault(i.Ci.IndicatorId), i.Version.Aggregation);

    private static decimal? BaselineFor(Snapshot s, Ind i, OrgUnit unit) =>
        s.Targets[i.Ci.IndicatorId].Where(t => t.UnitId == unit.Id && t.Kind == TargetKind.Baseline).OrderByDescending(t => t.Year).FirstOrDefault()?.Value;

    private static Performance Judge(Snapshot s, Ind i, OrgUnit unit, decimal near)
    {
        var target = TargetFor(s, i, unit);
        return ResultsRules.Status(i.Version.Aggregation, i.Version.Direction, Actual(s, i, unit), target.Value, target.NotStarted, near);
    }

    private static IndicatorResult Result(Snapshot s, Ind i, OrgUnit unit, Snapshot? previous, decimal near)
    {
        var agg = Actual(s, i, unit);
        var target = TargetFor(s, i, unit);
        var baseline = BaselineFor(s, i, unit);
        decimal? prev = null;
        if (previous?.Indicators.FirstOrDefault(x => x.Ci.IndicatorId == i.Ci.IndicatorId) is { } pi && previous.Units.TryGetValue(unit.Id, out var pu))
            prev = Actual(previous, pi, pu).Value;
        var who = unit.Kind == OrgUnitKind.University ? "الجامعة" : unit.NameAr;
        // للكلية في نسبة أو متوسط: مستهدف الجامعة بجانب مستهدفها، لتعرف قربها من نسبة الجامعة المرجوة.
        var uniTarget = unit.Kind != OrgUnitKind.University && Kpi.TargetLookup.ComparableAcrossLevels(i.Version.Aggregation)
            ? Kpi.TargetLookup.University([.. s.Targets[i.Ci.IndicatorId]], s.Root.Id, s.Cycle.TargetYear, s.PlanStart.GetValueOrDefault(i.Ci.IndicatorId))
            : null;
        var note = target.NotStarted ? $"يبدأ مستهدف المؤشر في {target.Year}." : agg.Note;
        return new IndicatorResult(i.Ci.IndicatorId, i.Ci.Code, i.Version.NameAr, i.Standard.Code, i.Version.Unit, i.Version.Direction, i.Ci.Level,
            agg.Value, target.Value, baseline, prev, ResultsRules.Achievement(i.Version.Direction, agg.Value, target.Value),
            ResultsRules.Status(i.Version.Aggregation, i.Version.Direction, agg, target.Value, target.NotStarted, near), agg.Reported, agg.Expected, note,
            ResultsRules.Summary(who, i.Version.Unit, i.Version.Direction, agg.Value, target.Value, prev),
            target.Source, target.Year, uniTarget, ResultsRules.Achievement(i.Version.Direction, agg.Value, uniTarget));
    }

    private static List<ResultUnit> VisibleUnits(Snapshot s, UnitScope scope)
    {
        var list = new List<ResultUnit>();
        if (scope.Covers(s.Root)) list.Add(new ResultUnit(s.Root.Id, s.Root.NameAr, s.Root.Kind));
        list.AddRange(s.Units.Values.Where(u => u.IsCollegeLevel && u.Active && scope.Covers(u)).OrderBy(u => u.SortOrder).ThenBy(u => u.NameAr)
            .Select(u => new ResultUnit(u.Id, u.NameAr, u.Kind)));
        return list;
    }

    private async Task<CollectionCycle?> PickCycleAsync(Guid? cycleId, CancellationToken ct)
    {
        var q = db.Cycles.AsNoTracking().Where(c => c.Status != CycleStatus.Draft);
        if (cycleId is { } id) return await q.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound();
        // الافتراضي: أحدث دورة فيها اعتماد، وإلا أحدث دورة.
        var cycles = await q.OrderByDescending(c => c.OpensOn).Take(5).ToListAsync(ct);
        var ids = cycles.Select(c => c.Id).ToList();
        var withApproved = await db.Submissions.AsNoTracking().Where(s => ids.Contains(s.CycleId) && s.Status == SubmissionStatus.Approved).Select(s => s.CycleId).Distinct().ToListAsync(ct);
        return cycles.FirstOrDefault(c => withApproved.Contains(c.Id)) ?? cycles.FirstOrDefault();
    }

    private async Task<CollectionCycle?> PreviousCycleAsync(CollectionCycle cycle, CancellationToken ct) =>
        await db.Cycles.AsNoTracking()
            .Where(c => c.Status != CycleStatus.Draft && c.Id != cycle.Id && c.Trial == cycle.Trial
                        && (c.OpensOn < cycle.OpensOn || (c.OpensOn == cycle.OpensOn && c.CreatedAt < cycle.CreatedAt)))
            .OrderByDescending(c => c.OpensOn).ThenByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    private async Task<decimal> NearAsync(CancellationToken ct) => await settings.IntAsync(SettingKeys.NearTargetPercent, ct) / 100m;

    private static ResultsCycle Cycle(CollectionCycle c) => new(c.Id, c.Name, c.PeriodLabel, c.TargetYear, c.Status, c.Trial);

    private static string? Fmt(decimal? v) => v is { } d ? ValueRules.Fmt(d) : null;
}
