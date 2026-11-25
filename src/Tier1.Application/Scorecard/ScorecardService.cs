using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Collection;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Scorecard;

/// <summary>مؤشر في بطاقة الأداء: قيم أرباعه العشرين وأحدث قيمة ودرجته الحالية ودرجات سنواته، كأعمدة «بطاقة مؤشرات الأداء».</summary>
public sealed record IndicatorScore(
    Guid Id, string Code, string Name, string? NameEn, string StandardCode, string Unit, Direction Direction, Accumulation Accumulation,
    IReadOnlyList<int> Perspectives, int? SdgPrimary, int? SdgSecondary, int? SdgInduced,
    decimal? FinalTarget, IReadOnlyList<decimal?> YearTargets, IReadOnlyList<decimal?> Quarters, decimal? Latest, int LatestIndex, decimal? Gap,
    decimal Score, decimal? RawAchievement, Level Level,
    IReadOnlyList<decimal?> YearValues, IReadOnlyList<decimal> YearScores, IReadOnlyList<decimal?> YearScoresVsYearly, IReadOnlyList<decimal?> QuarterScores);

public sealed record StandardScore(
    string Code, string Name, decimal Score, IReadOnlyList<decimal> YearScores, IReadOnlyList<decimal> YearScoresVsYearly, IReadOnlyList<IndicatorScore> Indicators);

public sealed record PillarScore(
    Guid Id, string Code, string Name, string? NameEn, decimal Score, int Tier, Rag Rag, int Indicators, int Evaluated, int InLevel,
    IReadOnlyList<decimal> YearScores, IReadOnlyList<decimal> YearScoresVsYearly, IReadOnlyList<StandardScore> Standards);

/// <summary>فئة بحدودها: للركيزة (جدول 1) وللدرجة الكلية (جدول 2)، والنقاط اللازمة لبلوغها.</summary>
public sealed record TierBand(int Tier, string NameAr, string NameEn, decimal PillarMin, decimal PillarMax, decimal OverallMin, decimal OverallMax, decimal Gap);

public sealed record OverallScore(
    decimal Score, TierResult Tier, string? WeakestPillar, decimal WeakestScore,
    IReadOnlyList<decimal> YearScores, IReadOnlyList<decimal> YearScoresVsYearly, IReadOnlyList<TierResult> YearTiers,
    int Indicators, int Reported, int InLevel);

public sealed record PerspectiveScore(
    int Number, string NameAr, string NameEn, int Indicators, int Evaluated, int InLevel, int Below, decimal? InLevelPercent,
    IReadOnlyList<decimal?> YearPercent, IReadOnlyList<decimal?> YearPercentVsYearly);

public sealed record SdgScore(
    int Number, string NameAr, string NameEn, int Primary, int Secondary, int Induced, int Total, int Evaluated, int InLevel, int Below,
    decimal? InLevelPercent, Rag? Rag, IReadOnlyList<decimal?> YearPercent);

public sealed record ScorecardDto(
    IReadOnlyList<int> Years, IReadOnlyList<PlanQuarter> Quarters, PlanQuarter? LastReported, TierThresholds Thresholds,
    OverallScore Overall, IReadOnlyList<TierBand> Tiers, IReadOnlyList<PillarScore> Pillars,
    IReadOnlyList<PerspectiveScore> Perspectives, IReadOnlyList<SdgScore> Sdgs, bool CanExport, IReadOnlyList<string>? ExportOmitted = null);

/// <summary>
/// بطاقة أداء الجامعة وفئتها بمعادلات الإطار الشامل (ScorecardRules)، من القيم الرسمية للجامعة في كل ربع (ينقلها مسئول ملف Tier 1).
/// تعرض لمن يرى النتائج.
/// </summary>
public sealed class ScorecardService(IScorecardStore db, IReadModel read, IAccessService access, IClock clock, IAuditLog audit, Settings.SettingsService settings, Settings.PlanPeriodService planPeriod,
    IKpiWorkbook workbook)
{
    private const string Feature = Features.Results;

    public async Task<ScorecardDto> GetAsync(CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        // بطاقة الجامعة كاملة لمن نطاقه الجامعة؛ مستخدم الكلية أو الإدارة يرى تقدم جهته مقابل مستهدف الجامعة فقط.
        if (!Access.UnitScope.For(me, Feature, Cap.View).All) throw DomainException.Forbidden();
        return await ComputeAsync(me.HasSpecial(Feature, "export") && Access.UnitScope.ForSpecial(me, Feature, "export").All, ct);
    }

    /// <summary>قيم الأرباع لكل مؤشر بكوده، لتعبئة ملف KPI.xlsx نفسه.</summary>
    public async Task<(IReadOnlyDictionary<string, KpiRow> Rows, string Label)> ExportValuesAsync(CancellationToken ct)
    {
        var me = await access.RequireSpecialAsync(Feature, "export", ct);
        // G-16 (system integration review 2026-11-24): the council's file is the whole university's card, as the page is.
        if (!Access.UnitScope.ForSpecial(me, Feature, "export").All) throw DomainException.Forbidden();
        var card = await ComputeAsync(true, ct);
        var map = card.Pillars.SelectMany(p => p.Standards).SelectMany(s => s.Indicators)
            .ToDictionary(i => i.Code, i => new KpiRow(i.Quarters, i.YearTargets, i.Direction == Direction.LowerIsBetter, i.Accumulation == Accumulation.Cumulative, i.Unit));
        await audit.RecordNowAsync(AuditEvents.Access("dc", Feature, "dc.scorecard_exported", "تصدير بطاقة مؤشرات الأداء بشكل ملف المجلس", "Exported the KPI card workbook") with
        {
            Target = new AuditTarget("scorecard", null, card.LastReported?.Label),
        }, ct);
        return (map, card.LastReported is { } q ? $"Y{q.YearIndex}-Q{q.Quarter}" : "empty");
    }

    public async Task<IReadOnlyList<PlanQuarter>> CalendarAsync(CancellationToken ct)
    {
        await access.CurrentAsync(ct);
        return (await planPeriod.CurrentAsync(ct)).Quarters(CairoTime.DateOf(clock.UtcNow));
    }

    private async Task<TierThresholds> ThresholdsAsync(CancellationToken ct) => new(
        await settings.IntAsync(SettingKeys.Tier1Min, ct), await settings.IntAsync(SettingKeys.Tier2Min, ct),
        await settings.IntAsync(SettingKeys.Tier3Min, ct), await settings.IntAsync(SettingKeys.Tier4Min, ct),
        await settings.IntAsync(SettingKeys.OverallTierOffset, ct));

    /// <summary>البطاقة دون فحص صلاحية (يفحصها المستدعي).</summary>
    internal async Task<ScorecardDto> ComputeAsync(bool canExport, CancellationToken ct)
    {
        var thresholds = await ThresholdsAsync(ct);
        // ADR-0017: the plan period in force gives the quarters and the plan years.
        var plan = await planPeriod.CurrentAsync(ct);
        var Years = plan.Years;
        var quarters = await CalendarAsync(ct);
        var pillars = await read.Query<Pillar>().AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        var standards = await read.Query<Standard>().AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var indicators = await read.Query<Indicator>().AsNoTracking().Include(i => i.Versions).Where(i => !i.Suspended).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var university = await read.Query<OrgUnit>().AsNoTracking().Where(u => u.Kind == OrgUnitKind.University).Select(u => u.Id).FirstOrDefaultAsync(ct);
        // بطاقة الأداء تقاس بالمستهدفات المعتمدة وحدها (D3).
        var targets = (await read.Query<IndicatorTarget>().AsNoTracking().Where(t => t.UnitId == university && t.Kind == TargetKind.Target && t.Status == TargetStatus.Approved).ToListAsync(ct)).ToLookup(t => t.IndicatorId);
        // القيم الرسمية التي نقلها مسئول ملف Tier 1 بعد مراجعة الكليات، لا المحسوبة لحظيًا: لا يدخل البطاقة رقم لم يراجع.
        var values = (await db.UniversityValues.AsNoTracking().ToListAsync(ct))
            .GroupBy(v => (v.Year, v.Quarter))
            .ToDictionary(g => g.Key, g => g.ToDictionary(v => v.IndicatorId, v => v.Value));

        var lastSlot = values.Where(v => v.Value.Values.Any(x => x is not null)).Select(v => plan.Slot(v.Key.Year, v.Key.Quarter)).DefaultIfEmpty(-1).Max();

        IndicatorScore Score(Indicator i, string standardCode)
        {
            var v = i.Effective ?? i.Current;
            var mine = targets[i.Id].ToList();
            var yearTargets = Years.Select(y => mine.FirstOrDefault(t => t.Year == y)?.Value).ToList();
            var final = yearTargets[^1] ?? yearTargets.LastOrDefault(t => t is not null);
            var series = new decimal?[plan.QuarterCount];
            foreach (var ((year, q), vals) in values)
            {
                var slot = plan.Slot(year, q);
                if (slot >= 0 && vals.TryGetValue(i.Id, out var x)) series[slot] = x;
            }
            var (latest, index) = ScorecardRules.Latest(series);
            var yearValues = Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.YearValue(series.Skip(y * 4).Take(4).ToList(), v.Accumulation)).ToList();
            return new IndicatorScore(
                i.Id, i.Code, v.NameAr, v.NameEn, standardCode, v.Unit, v.Direction, v.Accumulation,
                i.Perspectives, i.SdgPrimary, i.SdgSecondary, i.SdgInduced,
                final, yearTargets, series, latest, index, latest is { } l && final is { } f ? l - f : null,
                ScorecardRules.Achievement(latest, final, v.Direction) ?? 0, ScorecardRules.RawAchievement(latest, final, v.Direction),
                ScorecardRules.LevelOf(latest, final, v.Direction),
                yearValues,
                yearValues.Select(y => ScorecardRules.Achievement(y, final, v.Direction) ?? 0).ToList(),
                yearValues.Select((y, k) => ScorecardRules.Achievement(y, yearTargets[k], v.Direction)).ToList(),
                series.Select(q => ScorecardRules.Achievement(q, final, v.Direction)).ToList());
        }

        var pillarScores = new List<PillarScore>();
        foreach (var p in pillars)
        {
            var stds = standards.Where(s => s.PillarId == p.Id).Select(s =>
            {
                var list = indicators.Where(i => i.StandardId == s.Id).Select(i => Score(i, s.Code)).ToList();
                return new StandardScore(s.Code, s.NameAr, ScorecardRules.Average(list.Select(x => x.Score)),
                    Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.Average(list.Select(x => x.YearScores[y]))).ToList(),
                    Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.AverageKnown(list.Select(x => x.YearScoresVsYearly[y]))).ToList(),
                    list);
            }).Where(s => s.Indicators.Count > 0).ToList();
            if (stds.Count == 0) continue;
            var all = stds.SelectMany(s => s.Indicators).ToList();
            var score = ScorecardRules.Average(stds.Select(s => s.Score));
            pillarScores.Add(new PillarScore(p.Id, p.Code, p.NameAr, p.NameEn, score, ScorecardRules.PillarTier(score, thresholds), ScorecardRules.RagOf(score),
                all.Count, all.Count(x => x.Level != Level.NoData), all.Count(x => x.Level == Level.InLevel),
                Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.Average(stds.Select(s => s.YearScores[y]))).ToList(),
                Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.Average(stds.Select(s => s.YearScoresVsYearly[y]))).ToList(),
                stds));
        }

        var everyone = pillarScores.SelectMany(p => p.Standards).SelectMany(s => s.Indicators).ToList();
        var overall = ScorecardRules.Average(pillarScores.Select(p => p.Score));
        var weakest = pillarScores.OrderBy(p => p.Score).ThenBy(p => p.Code, StringComparer.Ordinal).FirstOrDefault();
        var yearScores = Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.Average(pillarScores.Select(p => p.YearScores[y]))).ToList();
        var yearVsYearly = Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.Average(pillarScores.Select(p => p.YearScoresVsYearly[y]))).ToList();
        var yearTiers = Enumerable.Range(0, plan.YearCount).Select(y => ScorecardRules.Tier(yearVsYearly[y], pillarScores.Select(p => p.YearScoresVsYearly[y]).DefaultIfEmpty(0).Min(), thresholds)).ToList();

        var overallDto = new OverallScore(overall, ScorecardRules.Tier(overall, weakest?.Score ?? 0, thresholds), weakest?.Name, weakest?.Score ?? 0,
            yearScores, yearVsYearly, yearTiers, everyone.Count, everyone.Count(x => x.Latest is not null), everyone.Count(x => x.Level == Level.InLevel));

        var bands = FrameworkNames.Tiers.Select(t =>
        {
            var pMin = thresholds.PillarMin(t.Number);
            var pMax = t.Number == 1 ? 100 : thresholds.PillarMin(t.Number - 1) - 1;
            var oMin = t.Number == 5 ? thresholds.OverallOffset : thresholds.OverallMin(t.Number);
            var oMax = t.Number == 1 ? 100 : Math.Min(thresholds.OverallMin(t.Number - 1) - 1, 100);
            return new TierBand(t.Number, t.NameAr, t.NameEn, pMin, pMax, oMin, oMax, t.Number == 5 ? 0 : ScorecardRules.GapTo(t.Number, overall, thresholds));
        }).ToList();

        var perspectives = FrameworkNames.Perspectives.Select(n =>
        {
            var linked = everyone.Where(x => x.Perspectives.Contains(n.Number)).ToList();
            var evaluated = linked.Count(x => x.Level != Level.NoData);
            var inLevel = linked.Count(x => x.Level == Level.InLevel);
            return new PerspectiveScore(n.Number, n.NameAr, n.NameEn, linked.Count, evaluated, inLevel, linked.Count(x => x.Level == Level.Below),
                evaluated == 0 ? null : ScorecardRules.Round1(100m * inLevel / linked.Count),
                Enumerable.Range(0, plan.YearCount).Select(y => linked.Any(x => x.YearValues[y] is not null) ? ScorecardRules.Round1(100m * linked.Count(x => x.YearScores[y] >= 100) / linked.Count) : (decimal?)null).ToList(),
                Enumerable.Range(0, plan.YearCount).Select(y =>
                {
                    var judged = linked.Where(x => x.YearScoresVsYearly[y] is not null).ToList();
                    return judged.Count == 0 ? (decimal?)null : ScorecardRules.Round1(100m * judged.Count(x => x.YearScoresVsYearly[y] >= 100) / judged.Count);
                }).ToList());
        }).ToList();

        var sdgs = FrameworkNames.Sdgs.Select(n =>
        {
            var primary = everyone.Count(x => x.SdgPrimary == n.Number);
            var secondary = everyone.Count(x => x.SdgSecondary == n.Number);
            var induced = everyone.Count(x => x.SdgInduced == n.Number);
            var linked = everyone.Where(x => x.SdgPrimary == n.Number || x.SdgSecondary == n.Number || x.SdgInduced == n.Number).ToList();
            var total = primary + secondary + induced;
            var evaluated = linked.Count(x => x.Level != Level.NoData);
            var inLevel = linked.Count(x => x.Level == Level.InLevel);
            decimal? pct = evaluated == 0 || total == 0 ? null : ScorecardRules.Round1(100m * inLevel / total);
            return new SdgScore(n.Number, n.NameAr, n.NameEn, primary, secondary, induced, total, evaluated, inLevel, linked.Count(x => x.Level == Level.Below),
                pct, pct is { } v ? ScorecardRules.RagOf(v) : null,
                Enumerable.Range(0, plan.YearCount).Select(y => linked.Any(x => x.YearValues[y] is not null) ? ScorecardRules.Round1(100m * linked.Count(x => x.YearScores[y] >= 100) / linked.Count) : (decimal?)null).ToList());
        }).ToList();

        // G-10 (system integration review 2026-11-24): indicators added in the system have no row in the council's workbook, which the
        // export left out silently. They are named beside the export button.
        var rows = workbook.Codes.ToHashSet(StringComparer.Ordinal);
        var omitted = canExport ? pillarScores.SelectMany(p => p.Standards).SelectMany(s => s.Indicators).Select(i => i.Code).Where(c => !rows.Contains(c)).ToList() : [];
        return new ScorecardDto(Years, quarters, lastSlot >= 0 ? quarters[lastSlot] : null, thresholds, overallDto, bands, pillarScores, perspectives, sdgs, canExport, omitted);
    }
}
