using System.Text.Json;
using Tier1.Application.Scorecard;
using Tier1.Domain.Kpi;

namespace Tier1.Tests;

/// <summary>
/// معادلات «الإطار الشامل لتقييم أداء الجامعات» (KPI.xlsx): كل حالة هنا محسوبة باليد من صيغ الملف
/// (AS للإنجاز الحالي، وAZ–BD للسنوات، وCL–CO مقابل مستهدف السنة، وحاسبة التصنيف).
/// </summary>
public class ScorecardRulesTests
{
    private static readonly TierThresholds T = TierThresholds.Default;

    [Theory]
    // تصاعدي: القيمة ÷ المستهدف × 100، بحد أقصى 100 ومقربة لرقم عشري.
    [InlineData(80, 90, Direction.HigherIsBetter, 88.9)]
    [InlineData(95, 90, Direction.HigherIsBetter, 100)]
    // تنازلي: المستهدف ÷ القيمة (نسبة الطلاب إلى الأعضاء 30 مقابل 25).
    [InlineData(30, 25, Direction.LowerIsBetter, 83.3)]
    [InlineData(20, 25, Direction.LowerIsBetter, 100)]
    [InlineData(0, 25, Direction.LowerIsBetter, 100)]
    // مستهدف صفر: التنازلي 100 − القيمة، والتصاعدي محقق ما لم تكن القيمة سالبة.
    [InlineData(12, 0, Direction.LowerIsBetter, 88)]
    [InlineData(3, 0, Direction.HigherIsBetter, 100)]
    [InlineData(-2, 0, Direction.HigherIsBetter, 0)]
    public void Achievement_follows_the_workbook(decimal value, decimal target, Direction direction, decimal expected) =>
        Assert.Equal(expected, ScorecardRules.Achievement(value, target, direction));

    [Fact]
    public void No_value_or_no_target_has_no_achievement_and_the_uncapped_rate_is_kept_for_information()
    {
        Assert.Null(ScorecardRules.Achievement(null, 90, Direction.HigherIsBetter));
        Assert.Null(ScorecardRules.Achievement(80, null, Direction.HigherIsBetter));
        Assert.Equal(105.6m, ScorecardRules.RawAchievement(95, 90, Direction.HigherIsBetter));
    }

    [Fact]
    public void Year_value_is_the_last_quarter_for_cumulative_and_the_average_for_snapshot()
    {
        decimal?[] quarters = [10, null, 30, null];
        Assert.Equal(30, ScorecardRules.YearValue(quarters, Accumulation.Cumulative));
        Assert.Equal(20, ScorecardRules.YearValue(quarters, Accumulation.Snapshot));
        Assert.Null(ScorecardRules.YearValue([null, null, null, null], Accumulation.Snapshot));
    }

    [Fact]
    public void Latest_value_is_the_last_entered_quarter()
    {
        var series = new decimal?[20];
        series[1] = 5;
        series[6] = 7;
        Assert.Equal((7m, 6), ScorecardRules.Latest(series));
        Assert.Equal((null, -1), ScorecardRules.Latest(new decimal?[20]));
    }

    [Fact]
    public void Level_compares_the_latest_value_with_the_final_target_in_the_indicator_direction()
    {
        Assert.Equal(Level.InLevel, ScorecardRules.LevelOf(90, 90, Direction.HigherIsBetter));
        Assert.Equal(Level.Below, ScorecardRules.LevelOf(89, 90, Direction.HigherIsBetter));
        Assert.Equal(Level.InLevel, ScorecardRules.LevelOf(24, 25, Direction.LowerIsBetter));
        Assert.Equal(Level.NoData, ScorecardRules.LevelOf(null, 25, Direction.LowerIsBetter));
    }

    [Theory]
    [InlineData(90, 1)]
    [InlineData(89.9, 2)]
    [InlineData(75, 2)]
    [InlineData(60, 3)]
    [InlineData(45, 4)]
    [InlineData(44.9, 5)]
    public void Overall_tiers_use_the_pillar_thresholds_raised_by_five(decimal score, int tier) =>
        Assert.Equal(tier, ScorecardRules.OverallTier(score, T));

    [Fact]
    public void The_weakest_pillar_lowers_the_tier_only_when_it_is_two_tiers_worse()
    {
        // الدرجة الكلية 78 (الفئة 2)، وأضعف ركيزة 50 (الفئة 4): تفاوت فئتين فتخفض إلى الفئة 3.
        Assert.Equal(new TierResult(2, 4, 3, TierAlert.Downgraded), ScorecardRules.Tier(78, 50, T));
        // أضعف ركيزة 60 (الفئة 3): فئة واحدة، تنبيه دون تخفيض.
        Assert.Equal(new TierResult(2, 3, 2, TierAlert.Warning), ScorecardRules.Tier(78, 60, T));
        Assert.Equal(new TierResult(2, 2, 2, TierAlert.Balanced), ScorecardRules.Tier(78, 75, T));
        // لا أدنى من الفئة 5.
        Assert.Equal(5, ScorecardRules.Tier(10, 0, T).FinalTier);
    }

    [Fact]
    public void Gaps_and_averages_round_like_the_workbook()
    {
        Assert.Equal(11.6m, ScorecardRules.GapTo(1, 78.4m, T));
        Assert.Equal(0, ScorecardRules.GapTo(3, 78.4m, T));
        // المؤشر بلا قيمة صفر في المتوسط (درجة المعيار)، ويستبعد في المقارنة بمستهدف السنة.
        Assert.Equal(50, ScorecardRules.Average([100, 0]));
        Assert.Equal(100, ScorecardRules.AverageKnown([100, null]));
        Assert.Equal(0, ScorecardRules.AverageKnown([null, null]));
    }

    [Fact]
    public void The_quarter_calendar_starts_from_the_configured_month()
    {
        var q = PlanPeriod.Default.Quarters(new DateOnly(2026, 11, 12));
        Assert.Equal(20, q.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), q[0].Start);
        Assert.Equal(new DateOnly(2026, 11, 30), q[0].End);
        Assert.True(q[0].Current);
        Assert.Equal((2028, 2), (q[5].Year, q[5].Quarter));
        Assert.Equal(new DateOnly(2031, 6, 1), q[19].Start);
    }

    /// <summary>AC-P1 (ADR-0017): the default plan runs 1 September 2026 – 31 August 2031: five plan years named 2027–2031, twenty quarters.</summary>
    [Fact]
    public void AC_P1_the_default_plan_is_september_2026_to_august_2031()
    {
        var p = PlanPeriod.Default;
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2031, 8, 31)), (p.Start, p.End));
        Assert.Equal([2027, 2028, 2029, 2030, 2031], p.Years);
        Assert.Equal((20, 2026), (p.QuarterCount, p.BaselineYear));
        Assert.Equal(19, p.Slot(2031, 4));
        Assert.Equal(-1, p.Slot(2032, 1));
    }

    /// <summary>AC-P2: another period from the ministry moves every quarter and plan year with it.</summary>
    [Fact]
    public void AC_P2_a_changed_period_moves_the_quarters_and_years()
    {
        var p = new PlanPeriod(2027, 1, 2032, 12);
        Assert.Null(PlanPeriod.Problem(p));
        Assert.Equal([2027, 2028, 2029, 2030, 2031, 2032], p.Years);
        var q = p.Quarters(new DateOnly(2032, 12, 31));
        Assert.Equal((24, new DateOnly(2032, 10, 1), true), (q.Count, q[^1].Start, q[^1].Current));
        Assert.Equal(2026, p.BaselineYear);
    }

    /// <summary>AC-P3: the plan is whole plan years of twelve months, from one to ten.</summary>
    [Theory]
    [InlineData(2026, 9, 2031, 7)]   // a partial last year
    [InlineData(2026, 9, 2026, 12)]  // under a year
    [InlineData(2026, 9, 2037, 8)]   // eleven years
    [InlineData(2026, 13, 2031, 8)]  // no month 13
    public void AC_P3_a_period_that_is_not_whole_plan_years_is_refused(int sy, int sm, int ey, int em) =>
        Assert.NotNull(PlanPeriod.Problem(new PlanPeriod(sy, sm, ey, em)));
}

/// <summary>سلامة بذرة الإطار الشامل المستخرجة من KPI.xlsx (Tier1.Tools --import-kpi).</summary>
public class KpiFrameworkSeedTests
{
    private static JsonElement Seed()
    {
        using var stream = typeof(Tier1.Infrastructure.DependencyInjection).Assembly
            .GetManifestResourceStream("Tier1.Infrastructure.Persistence.Seed.kpi-framework.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream!).RootElement.Clone();
    }

    [Fact]
    public void The_framework_has_six_pillars_twelve_standards_and_ninety_two_indicators_with_five_yearly_targets()
    {
        var seed = Seed();
        Assert.Equal(6, seed.GetProperty("pillars").GetArrayLength());
        Assert.Equal(12, seed.GetProperty("standards").GetArrayLength());
        var indicators = seed.GetProperty("indicators").EnumerateArray().ToList();
        Assert.Equal(92, indicators.Count);
        Assert.Equal(92, indicators.Select(i => i.GetProperty("code").GetString()).Distinct().Count());
        Assert.All(indicators, i => Assert.Equal(5, i.GetProperty("targets").GetArrayLength()));
        Assert.All(indicators, i => Assert.InRange(i.GetProperty("perspectives").GetArrayLength(), 1, 7));
    }

    [Fact]
    public void The_agreed_corrections_are_applied()
    {
        var byCode = Seed().GetProperty("indicators").EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!);
        Assert.Equal("LowerIsBetter", byCode["7.03"].GetProperty("direction").GetString());
        Assert.Equal(78, byCode["8.08"].GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("year").GetInt32() == 2029).GetProperty("value").GetDecimal());
        Assert.DoesNotContain("%", byCode["7.05"].GetProperty("unit").GetString());
        Assert.DoesNotContain("م²", byCode["11.02"].GetProperty("unit").GetString());
        // نسبة الجامعة من البسط والمقام: كل مؤشر نسبة يحدد بسطه ومقامه.
        Assert.All(byCode.Values.Where(i => i.GetProperty("aggregation").GetString() == "RatioOfSums"),
            i => Assert.False(string.IsNullOrWhiteSpace(i.GetProperty("denominator").GetString())));
    }

    [Theory]
    [InlineData("66.66666666666666666666666667", "66.6667")]
    [InlineData("0.00005", "0.0001")]
    [InlineData("-0.00005", "-0.0001")]
    [InlineData("12", "12")]
    public void Official_values_are_kept_at_four_decimals(string computed, string stored)
    {
        // DB-6: stored and compared at the column's precision, so a recomputed value equal to the stored one stays "transferred".
        var c = decimal.Parse(computed, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), Tier1.Application.Scorecard.ReviewService.Round4(c));
        Assert.Null(Tier1.Application.Scorecard.ReviewService.Round4(null));
    }
}
