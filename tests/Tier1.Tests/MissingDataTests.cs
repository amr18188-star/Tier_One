using Tier1.Application.Collection;
using Tier1.Domain.Collection;
using Tier1.Domain.Kpi;

namespace Tier1.Tests;

/// <summary>
/// D6 (ADR-0005), requirement 4-10-1: missing data is a state of its own, never poor performance.
/// "Not available" is missing (not a number, not zero); "not applicable" leaves the unit out of what is due;
/// any aggregate (sum, ratio, average) from part of the due units is "partial data" and is not judged.
/// </summary>
public class MissingDataTests
{
    private static UnitValue N(decimal value, decimal? n = null, decimal? d = null) => new(Guid.NewGuid(), ValueKind.Number, value, n, d);
    private static UnitValue NotAvailable() => new(Guid.NewGuid(), ValueKind.NotAvailable, null, null, null);
    private static UnitValue NotApplicable() => new(Guid.NewGuid(), ValueKind.NotApplicable, null, null, null);

    [Fact]
    public void Not_available_is_missing_so_a_sum_stays_partial_and_is_not_judged_behind()
    {
        // Before D6 the "not available" unit counted as reported, the sum looked complete and was judged behind.
        var a = ResultsRules.Combine(Aggregation.Sum, false, [N(300), N(200), NotAvailable()], 3);
        Assert.Equal(500m, a.Value);
        Assert.Equal((2, 3, 1), (a.Reported, a.Expected, a.NotAvailable));
        Assert.False(a.Complete);
        Assert.Contains("«غير متاح» من 1", a.Note);
        Assert.Equal(Performance.Incomplete, ResultsRules.Status(Aggregation.Sum, Direction.HigherIsBetter, a, 1000, false));
    }

    [Fact]
    public void Not_applicable_leaves_the_unit_out_so_the_rest_is_complete_and_judged()
    {
        var a = ResultsRules.Combine(Aggregation.Sum, false, [N(600), N(500), NotApplicable()], 3);
        Assert.Equal((2, 2, 1), (a.Reported, a.Expected, a.NotApplicable));
        Assert.True(a.Complete);
        Assert.Null(a.Note);
        Assert.Equal(Performance.Achieved, ResultsRules.Status(Aggregation.Sum, Direction.HigherIsBetter, a, 1000, false));
    }

    [Fact]
    public void A_ratio_from_part_of_the_units_is_shown_but_not_judged()
    {
        var a = ResultsRules.Combine(Aggregation.RatioOfSums, true, [N(90, 45, 50)], 3);
        Assert.Equal(90m, a.Value);
        Assert.Contains("نسبة جزئي", a.Note);
        Assert.Contains("1 من 3", a.Note);
        Assert.Equal(Performance.Incomplete, ResultsRules.Status(Aggregation.RatioOfSums, Direction.HigherIsBetter, a, 75, false));
    }

    [Fact]
    public void A_weighted_average_from_part_of_the_units_is_not_judged()
    {
        var a = ResultsRules.Combine(Aggregation.WeightedAverage, true, [N(80, d: 10), NotAvailable()], 2);
        Assert.Equal(80m, a.Value);
        Assert.Equal(Performance.Incomplete, ResultsRules.Status(Aggregation.WeightedAverage, Direction.HigherIsBetter, a, 70, false));
    }

    [Fact]
    public void Only_not_available_values_mean_no_data_not_zero()
    {
        var a = ResultsRules.Combine(Aggregation.Sum, false, [NotAvailable(), NotAvailable()], 2);
        Assert.Null(a.Value);
        Assert.Equal((0, 2, 2), (a.Reported, a.Expected, a.NotAvailable));
        Assert.Equal(Performance.NoData, ResultsRules.Status(Aggregation.Sum, Direction.HigherIsBetter, a, 100, false));
    }

    [Fact]
    public void Complete_data_is_judged_as_before()
    {
        var a = ResultsRules.Combine(Aggregation.RatioOfSums, true, [N(90, 45, 50), N(10, 10, 100)], 2);
        Assert.True(a.Complete);
        Assert.Null(a.Note);
        Assert.Equal(Performance.Behind, ResultsRules.Status(Aggregation.RatioOfSums, Direction.HigherIsBetter, a, 75, false));
    }

    [Fact]
    public void A_single_central_unit_is_judged_on_its_own_value()
    {
        var a = ResultsRules.Combine(Aggregation.Central, true, [N(70)], 1);
        Assert.True(a.Complete);
        Assert.Equal(Performance.Near, ResultsRules.Status(Aggregation.Central, Direction.HigherIsBetter, a, 75, false));
        var na = ResultsRules.Combine(Aggregation.Central, true, [NotAvailable()], 1);
        Assert.Equal(Performance.NoData, ResultsRules.Status(Aggregation.Central, Direction.HigherIsBetter, na, 75, false));
    }
}
