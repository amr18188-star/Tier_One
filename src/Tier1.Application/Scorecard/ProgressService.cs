using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Collection;
using Tier1.Application.Kpi;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Scorecard;

/// <summary>مؤشر في تقدم الكلية: قيمها في الأرباع، ومستهدف الجامعة ومستهدفها هي، وبعدها عن مستهدف الجامعة، وقيمة الجامعة الرسمية.</summary>
public sealed record ProgressIndicator(
    Guid Id, string Code, string Name, string StandardCode, string Unit, Direction Direction, Accumulation Accumulation,
    IReadOnlyList<decimal?> Quarters, decimal? Latest, int LatestIndex,
    IReadOnlyList<decimal?> UniversityTargets, IReadOnlyList<decimal?> CollegeTargets, IReadOnlyList<decimal?> UniversityQuarters,
    decimal? YearTarget, decimal? Gap, decimal? YearAchievement, decimal? OwnTarget, decimal? OwnAchievement,
    decimal? Score, Level Level, IReadOnlyList<decimal?> YearValues, IReadOnlyList<decimal?> YearScores,
    bool Comparable, decimal? Share, decimal? UniversityYearTarget);

/// <summary>درجة المعيار متوسط مؤشراته التي لها مستهدف تقارن به الكلية.</summary>
public sealed record ProgressStandard(string Code, string Name, decimal Score, IReadOnlyList<ProgressIndicator> Indicators);

public sealed record ProgressPillar(Guid Id, string Code, string Name, int Index, decimal Score, decimal? UniversityScore, IReadOnlyList<ProgressStandard> Standards);

public sealed record ProgressDto(
    IReadOnlyList<ResultUnit> Units, ResultUnit Unit, PlanQuarter? LastReported, decimal Score, decimal? UniversityScore,
    int Indicators, int Reported, int InLevel, IReadOnlyList<ProgressPillar> Pillars, IReadOnlyList<PlanQuarter> Quarters);

/// <summary>
/// تقدم الكلية: قيمها المعتمدة في كل ربع لكل مؤشر يجمع منها، مقارنة بمستهدف الجامعة (وبمستهدفها الخاص إن وجد)، وكم تبعد عنه.
/// درجتها بقواعد بطاقة الجامعة نفسها (الإنجاز من المستهدف النهائي، والمعيار متوسط مؤشراته، والركيزة متوسط معاييرها) فتقارن بدرجة الجامعة.
/// كل مستخدم يرى كليات نطاقه فقط.
/// </summary>
public sealed class ProgressService(IScorecardStore db, IReadModel read,  IAccessService access, IClock clock, ResultsService results, ScorecardService scorecard, Settings.SettingsService settings,
    Organization.UnitIndicatorsService assignments)
{
    private static readonly int[] Years = PlanCalendar.Years;

    public async Task<ProgressDto?> GetAsync(Guid? unitId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Features.Results, Cap.View, ct);
        var scope = UnitScope.For(me, Features.Results, Cap.View);
        var all = await read.Query<OrgUnit>().AsNoTracking().ToListAsync(ct);
        var root = all.First(u => u.Kind == OrgUnitKind.University);
        var colleges = all.Where(u => u.IsCollegeLevel && u.Active && scope.Covers(u)).OrderBy(u => u.SortOrder).ThenBy(u => u.NameAr).ToList();
        if (colleges.Count == 0) return null;
        var unit = unitId is { } id ? colleges.FirstOrDefault(c => c.Id == id) ?? throw DomainException.NotFound() : colleges[0];

        var quarters = PlanCalendar.Quarters(await settings.IntAsync(SettingKeys.PlanStartYear, ct), await settings.IntAsync(SettingKeys.PlanStartMonth, ct), CairoTime.DateOf(clock.UtcNow));
        var values = await results.UnitQuartersAsync(unit.Id, ct);
        var official = (await db.UniversityValues.AsNoTracking().ToListAsync(ct)).ToLookup(v => v.IndicatorId);
        var pillars = await read.Query<Pillar>().AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        var standards = await read.Query<Standard>().AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var indicators = await read.Query<Indicator>().AsNoTracking().Include(i => i.Versions).Where(i => !i.Suspended).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var targets = (await read.Query<IndicatorTarget>().AsNoTracking().ToListAsync(ct)).ToLookup(t => t.IndicatorId);
        // درجة الجامعة الرسمية لكل ركيزة (من القيم المنقولة) للمقارنة: رقم مجمع يراه كل من يرى تقدم كليته، لتعرف أين تقف منه.
        var uniCard = await scorecard.ComputeAsync(false, ct);
        var map = await assignments.MapAsync(ct);

        ProgressIndicator? Score(Indicator i, string standardCode)
        {
            var v = i.Effective ?? i.Current;
            if (v.Level == CollectionLevel.University) return null;
            // ما لم تكلف به الكلية (هي أو أقسامها) لا يظهر في تقدمها ولا يدخل درجتها.
            if (!map.CountsFor(unit, new Organization.IndicatorRef(i.Id, v.Level, v.Aggregation == Aggregation.DistinctCount))) return null;
            var series = new decimal?[20];
            foreach (var ((year, q), vals) in values)
            {
                var slot = PlanCalendar.Slot(year, q);
                if (slot is >= 0 and < 20 && vals.TryGetValue(i.Id, out var a)) series[slot] = a.Value;
            }
            var uniSeries = new decimal?[20];
            foreach (var o in official[i.Id])
            {
                var slot = PlanCalendar.Slot(o.Year, o.Quarter);
                if (slot is >= 0 and < 20) uniSeries[slot] = o.Value;
            }
            var mine = targets[i.Id].ToList();
            var uniTargets = Years.Select(y => TargetLookup.University(mine, root.Id, y, i.PlanStartYear)).ToList();
            var ownTargets = Years.Select(y =>
            {
                var t = TargetLookup.For(mine, unit, root.Id, y, i.PlanStartYear, v.Aggregation);
                return t.Source is TargetSource.Unit or TargetSource.EachCollege ? t.Value : null;
            }).ToList();
            var (latest, index) = ScorecardRules.Latest(series);
            var yearIndex = index >= 0 ? index / 4 : -1;
            // النسب والمتوسطات: الكلية مقابل مستهدف الجامعة نفسه. المجموع والعدد: رقم الكلية جزء من رقم الجامعة فلا يقارن بمستهدفها كله؛
            // يقارن بمستهدف الكلية إن وضع، وإلا تظهر حصتها من قيمة الجامعة ولا يدخل درجتها.
            var comparable = TargetLookup.ComparableAcrossLevels(v.Aggregation);
            var against = comparable ? uniTargets : ownTargets;
            var uniYearTarget = yearIndex >= 0 ? uniTargets[yearIndex] : null;
            var yearTarget = yearIndex >= 0 ? against[yearIndex] : null;
            var ownTarget = yearIndex >= 0 ? ownTargets[yearIndex] : null;
            var final = against[^1];
            var yearValues = Enumerable.Range(0, 5).Select(y => ScorecardRules.YearValue(series.Skip(y * 4).Take(4).ToList(), v.Accumulation)).ToList();
            var share = !comparable && latest is { } part && index >= 0 && uniSeries[index] is { } whole && whole != 0 ? Math.Round(part / whole * 100, 1) : (decimal?)null;
            return new ProgressIndicator(i.Id, i.Code, v.NameAr, standardCode, v.Unit, v.Direction, v.Accumulation,
                series, latest, index, uniTargets, ownTargets, uniSeries,
                yearTarget, latest is { } l && yearTarget is { } t ? l - t : null, ScorecardRules.Achievement(latest, yearTarget, v.Direction),
                ownTarget, ScorecardRules.Achievement(latest, ownTarget, v.Direction),
                final is null ? null : ScorecardRules.Achievement(latest, final, v.Direction) ?? 0, ScorecardRules.LevelOf(latest, yearTarget, v.Direction),
                yearValues, yearValues.Select((y, k) => ScorecardRules.Achievement(y, against[k], v.Direction)).ToList(),
                comparable, share, uniYearTarget);
        }

        var result = new List<ProgressPillar>();
        foreach (var (p, index) in pillars.Select((p, k) => (p, k)))
        {
            var stds = standards.Where(s => s.PillarId == p.Id).Select(s =>
            {
                var list = indicators.Where(i => i.StandardId == s.Id).Select(i => Score(i, s.Code)).Where(x => x is not null).Select(x => x!).ToList();
                return new ProgressStandard(s.Code, s.NameAr, ScorecardRules.AverageKnown(list.Select(x => x.Score)), list);
            }).Where(s => s.Indicators.Count > 0).ToList();
            if (stds.Count == 0) continue;
            // المعيار الذي لا مؤشر فيه بمستهدف للكلية لا يدخل درجة ركيزتها.
            result.Add(new ProgressPillar(p.Id, p.Code, p.NameAr, index,
                ScorecardRules.Average(stds.Where(s => s.Indicators.Any(x => x.Score is not null)).Select(s => s.Score)),
                uniCard?.Pillars.FirstOrDefault(x => x.Id == p.Id)?.Score, stds));
        }

        var everyone = result.SelectMany(p => p.Standards).SelectMany(s => s.Indicators).ToList();
        var last = everyone.Select(x => x.LatestIndex).DefaultIfEmpty(-1).Max();
        return new ProgressDto(
            colleges.Select(c => new ResultUnit(c.Id, c.NameAr, c.Kind)).ToList(), new ResultUnit(unit.Id, unit.NameAr, unit.Kind),
            last >= 0 ? quarters[last] : null,
            ScorecardRules.Average(result.Where(p => p.Standards.Any(s => s.Indicators.Any(x => x.Score is not null))).Select(p => p.Score)), uniCard?.Overall.Score,
            everyone.Count, everyone.Count(x => x.Latest is not null), everyone.Count(x => x.Level == Level.InLevel), result, quarters);
    }
}
