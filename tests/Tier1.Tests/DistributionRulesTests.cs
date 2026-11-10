using Tier1.Application.Kpi;
using Tier1.Domain.Kpi;

namespace Tier1.Tests;

/// <summary>
/// توزيع مستهدفات الكليات مقابل مستهدف الجامعة. الحالات مأخوذة من تعارضات حقيقية في الملفين
/// (C-16: «مشروعان لكل كلية» مقابل 23 للجامعة).
/// </summary>
public class DistributionRulesTests
{
    private static CollegeShare Share(decimal? value, decimal? num = null, decimal? den = null) =>
        new(Guid.NewGuid(), "كلية", value, num, den, null, false);

    [Fact]
    public void Two_projects_for_each_of_24_colleges_exceed_the_university_target()
    {
        var colleges = Enumerable.Range(0, 24).Select(_ => Share(2)).ToList();
        var r = DistributionRules.Compare(Aggregation.Sum, Direction.HigherIsBetter, false, 23, colleges);
        Assert.Equal(DistributionStatus.Covers, r.Status);
        Assert.Equal(48, r.CollegesValue);
        Assert.Equal(25, r.Gap);
        Assert.Contains("أعلى بمقدار 25", r.SummaryAr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sum_below_the_target_is_short()
    {
        var r = DistributionRules.Compare(Aggregation.Sum, Direction.HigherIsBetter, false, 30, [Share(10), Share(12)]);
        Assert.Equal(DistributionStatus.Short, r.Status);
        Assert.Equal(-8, r.Gap);
    }

    [Fact]
    public void For_lower_is_better_a_smaller_total_covers_the_target()
    {
        var r = DistributionRules.Compare(Aggregation.Sum, Direction.LowerIsBetter, false, 100, [Share(40), Share(50)]);
        Assert.Equal(DistributionStatus.Covers, r.Status);
    }

    [Fact]
    public void A_college_without_a_target_makes_the_distribution_incomplete()
    {
        var r = DistributionRules.Compare(Aggregation.Sum, Direction.HigherIsBetter, false, 10, [Share(5), Share(null)]);
        Assert.Equal(DistributionStatus.Incomplete, r.Status);
        Assert.Equal(1, r.MissingCount);
    }

    [Fact]
    public void Ratios_are_combined_from_numerators_and_denominators_not_averaged()
    {
        // كلية صغيرة 90% (9/10) وكبيرة 50% (500/1000): متوسط النسب 70% خطأ، والنسبة المجمعة 50.5%.
        var r = DistributionRules.Compare(Aggregation.RatioOfSums, Direction.HigherIsBetter, true, 60, [Share(90, 9, 10), Share(50, 500, 1000)]);
        Assert.Equal(DistributionStatus.Short, r.Status);
        Assert.Equal(50.396m, Math.Round(r.CollegesValue!.Value, 3));
    }

    [Fact]
    public void A_ratio_without_numerators_cannot_be_compared()
    {
        var r = DistributionRules.Compare(Aggregation.RatioOfSums, Direction.HigherIsBetter, true, 60, [Share(70), Share(80)]);
        Assert.Equal(DistributionStatus.Incomplete, r.Status);
        Assert.Equal(2, r.MissingCount);
    }

    [Theory]
    [InlineData(Aggregation.DistinctCount)]
    [InlineData(Aggregation.Central)]
    [InlineData(Aggregation.Undefined)]
    public void Distinct_central_and_undefined_indicators_are_not_summed(Aggregation aggregation)
    {
        var r = DistributionRules.Compare(aggregation, Direction.HigherIsBetter, false, 40, [Share(36), Share(29)]);
        Assert.Equal(DistributionStatus.NotComparable, r.Status);
        Assert.Null(r.CollegesValue);
    }

    [Fact]
    public void No_university_target_is_reported_as_such()
    {
        var r = DistributionRules.Compare(Aggregation.Sum, Direction.HigherIsBetter, false, null, [Share(1)]);
        Assert.Equal(DistributionStatus.NoUniversityTarget, r.Status);
    }
}
