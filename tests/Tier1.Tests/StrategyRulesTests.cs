using Tier1.Application.Collection;
using Tier1.Application.Kpi;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Tests;

/// <summary>
/// دقة الحساب على مستوى الكلية والجامعة، والمستهدف الساري بحسب سنوات الخطة، وقواعد مدد المستهدفات والمبادرات.
/// </summary>
public class StrategyRulesTests
{
    private static readonly OrgUnit University = new() { Code = "CU", Kind = OrgUnitKind.University, Path = "/CU/" };
    private static readonly OrgUnit College = new() { Code = "ENG", Kind = OrgUnitKind.College, Path = "/CU/ENG/", ParentId = University.Id };

    private static IndicatorTarget T(OrgUnit unit, int year, decimal value, TargetKind kind = TargetKind.Target) =>
        new() { UnitId = unit.Id, Year = year, Value = value, Kind = kind };

    // ---------------------------------------------------------------- المستهدف الساري

    [Fact]
    public void Before_the_start_year_the_indicator_is_not_started()
    {
        var targets = new[] { T(University, 2029, 50), T(University, 2031, 80) };
        var info = TargetLookup.For(targets, University, University.Id, 2027, planStart: 2029, Aggregation.RatioOfSums);
        Assert.True(info.NotStarted);
        Assert.Null(info.Value);
        Assert.Equal(2029, info.Year);
    }

    [Fact]
    public void The_latest_target_up_to_the_year_applies_and_is_sustained_after_it_is_reached()
    {
        var targets = new[] { T(University, 2027, 60), T(University, 2028, 70) };
        Assert.Equal(60m, TargetLookup.For(targets, University, University.Id, 2027, null, Aggregation.Sum).Value);
        // هدف قصير المدى تحقق في 2028: يستمر بعده.
        var later = TargetLookup.For(targets, University, University.Id, 2030, null, Aggregation.Sum);
        Assert.Equal((70m, 2028), (later.Value!.Value, later.Year!.Value));
    }

    [Fact]
    public void A_college_without_its_own_target_is_compared_with_the_university_ratio_but_not_with_a_university_sum()
    {
        var targets = new[] { T(University, 2027, 85) };
        var ratio = TargetLookup.For(targets, College, University.Id, 2027, null, Aggregation.RatioOfSums);
        Assert.Equal((85m, TargetSource.University), (ratio.Value!.Value, ratio.Source));
        // مجموع الجامعة (175 ألف طالب) ليس مستهدفًا لكلية واحدة.
        Assert.Equal(TargetInfo.None, TargetLookup.For(targets, College, University.Id, 2027, null, Aggregation.Sum));
    }

    [Fact]
    public void The_college_own_target_then_the_each_college_target_come_before_the_university()
    {
        var each = T(University, 2027, 70, TargetKind.EachCollege);
        var targets = new List<IndicatorTarget> { T(University, 2027, 85), each };
        Assert.Equal(TargetSource.EachCollege, TargetLookup.For(targets, College, University.Id, 2029, null, Aggregation.RatioOfSums).Source);
        targets.Add(T(College, 2027, 78));
        var own = TargetLookup.For(targets, College, University.Id, 2027, null, Aggregation.RatioOfSums);
        Assert.Equal((78m, TargetSource.Unit), (own.Value!.Value, own.Source));
        // الجامعة لا تأخذ المستهدف الموحد للكليات.
        Assert.Equal(85m, TargetLookup.For(targets, University, University.Id, 2027, null, Aggregation.RatioOfSums).Value);
    }

    [Fact]
    public void A_central_unit_entering_the_university_figure_is_compared_with_the_university_target_even_for_a_sum()
    {
        var central = new OrgUnit { Code = "QAC", Kind = OrgUnitKind.Administration, Path = "/CU/QAC/" };
        var targets = new[] { T(University, 2027, 27), T(University, 2027, 2, TargetKind.EachCollege) };
        var info = TargetLookup.For(targets, central, University.Id, 2027, null, Aggregation.DistinctCount);
        Assert.Equal((27m, TargetSource.University), (info.Value!.Value, info.Source));
    }

    // ---------------------------------------------------------------- التجميع

    [Fact]
    public void University_ratio_comes_from_summed_numerators_and_denominators_not_the_average_of_college_ratios()
    {
        // الهندسة 45 من 50 (90%)، والتجارة 10 من 100 (10%): متوسط النسب 50% خطأ، والصحيح 55 من 150.
        var values = new List<UnitValue>
        {
            new(Guid.NewGuid(), ValueKind.Number, 90, 45, 50),
            new(Guid.NewGuid(), ValueKind.Number, 10, 10, 100),
        };
        var agg = ResultsRules.Combine(Aggregation.RatioOfSums, true, values, 2);
        Assert.Equal((55m, 150m), (agg.Numerator!.Value, agg.Denominator!.Value));
        Assert.Equal(36.6667m, agg.Value);
    }

    [Fact]
    public void Teaching_hours_per_member_are_total_hours_over_total_members()
    {
        // 12 ساعة لـ 300 عضو و16 ساعة لـ 100 عضو: الجامعة 13 لا 14.
        var values = new List<UnitValue>
        {
            new(Guid.NewGuid(), ValueKind.Number, 12, 3600, 300),
            new(Guid.NewGuid(), ValueKind.Number, 16, 1600, 100),
        };
        Assert.Equal(13m, ResultsRules.Combine(Aggregation.RatioOfSums, false, values, 2).Value);
    }

    [Fact]
    public void A_partial_sum_is_not_judged_as_behind()
    {
        var values = new List<UnitValue> { new(Guid.NewGuid(), ValueKind.Number, 400, null, null) };
        var agg = ResultsRules.Combine(Aggregation.Sum, false, values, 3);
        Assert.Contains("1 من 3", agg.Note);
        Assert.Equal(Performance.Incomplete, ResultsRules.Status(Aggregation.Sum, Direction.HigherIsBetter, agg, 1000, false));
        // النسبة من الجهات المعتمدة يحكم عليها مع ملاحظة.
        var ratio = new Aggregate(80, 80, 100, 1, 3, 0, null);
        Assert.Equal(Performance.Achieved, ResultsRules.Status(Aggregation.RatioOfSums, Direction.HigherIsBetter, ratio, 75, false));
        Assert.Equal(Performance.NotStarted, ResultsRules.Status(Aggregation.RatioOfSums, Direction.HigherIsBetter, ratio, null, true));
    }

    [Theory]
    [InlineData(12, 12, "Achieved")]
    [InlineData(12.5, 12, "Achieved")]
    [InlineData(11.5, 12, "Achieved")]
    [InlineData(13, 12, "Near")]
    [InlineData(10.9, 12, "Near")]
    [InlineData(14, 12, "Behind")]
    [InlineData(9, 12, "Behind")]
    public void Reaching_a_value_is_judged_by_distance_in_both_directions(decimal actual, decimal target, string expected)
    {
        Assert.Equal(Enum.Parse<Performance>(expected), ResultsRules.Judge(Direction.Range, actual, target));
    }

    [Fact]
    public void Reaching_a_value_counts_moving_closer_as_improvement()
    {
        var text = ResultsRules.Summary("الجامعة", "ساعة / أسبوع", Direction.Range, 13, 12, 14);
        Assert.Contains("بتحسن", text);
    }

    // ---------------------------------------------------------------- دقة التعريف

    [Theory]
    [InlineData("%", Aggregation.Sum, CollectionLevel.College, true)]
    [InlineData("طالب لكل عضو", Aggregation.Sum, CollectionLevel.College, true)]
    [InlineData("ساعة / أسبوع", Aggregation.Sum, CollectionLevel.College, true)]
    [InlineData("%", Aggregation.Central, CollectionLevel.College, true)]
    [InlineData("%", Aggregation.RatioOfSums, CollectionLevel.College, false)]
    [InlineData("عدد", Aggregation.Sum, CollectionLevel.College, false)]
    [InlineData("%", Aggregation.Central, CollectionLevel.University, false)]
    public void Rules_that_would_add_up_rates_cannot_be_approved(string unit, Aggregation aggregation, CollectionLevel level, bool problem)
    {
        var v = new IndicatorVersion { Unit = unit, Aggregation = aggregation, Level = level };
        Assert.Equal(problem, IndicatorRules.AggregationProblem(v) is not null);
    }

    [Fact]
    public void A_weighted_average_needs_its_weight_described_before_approval()
    {
        var v = new IndicatorVersion { Definition = "د", Unit = "%", Direction = Direction.HigherIsBetter, Aggregation = Aggregation.WeightedAverage, DataSource = "م", OwnerUnitId = Guid.NewGuid() };
        Assert.Equal(["أساس الترجيح"], IndicatorRules.MissingForApproval(v));
        v.Denominator = "عدد المستجيبين";
        Assert.Empty(IndicatorRules.MissingForApproval(v));
    }

    // ---------------------------------------------------------------- مدد الخطة

    [Fact]
    public void Plan_years_stay_inside_the_plan_and_in_order()
    {
        PlanRules.ValidateYears(2029, 2031, 2026, "فترة المستهدف");
        Assert.Equal("plan_year", Assert.Throws<DomainException>(() => PlanRules.ValidateYears(2026, 2031, 2026, "x")).Code);
        Assert.Equal("plan_order", Assert.Throws<DomainException>(() => PlanRules.ValidateYears(2030, 2028, 2026, "x")).Code);
    }

    [Theory]
    [InlineData(null, 2028, "قصير المدى (سنتان)، يتحقق في 2028")]
    [InlineData(null, null, "طويل المدى (5 سنوات)، يتحقق في 2031")]
    [InlineData(2029, 2031, "متوسط المدى (3 سنوات)، يبدأ في 2029 ويتحقق في 2031")]
    public void The_horizon_is_described_in_words(int? start, int? end, string expected)
    {
        Assert.Equal(expected, PlanRules.Horizon(start, end, 2027, 2031));
    }

    [Fact]
    public void New_codes_follow_the_highest_number_and_are_never_reused()
    {
        Assert.Equal(7, StrategyService.NextNumber(["P1", "P2", "P6"], "P"));
        Assert.Equal(13, StrategyService.NextNumber(["S01", "S12"], "S"));
        Assert.Equal(1, StrategyService.NextNumber([], "M"));
    }

    // ---------------------------------------------------------------- الإدخال

    [Fact]
    public void Distinct_counts_are_entered_once_for_the_whole_university_and_for_the_college_of_department_counts()
    {
        var central = new OrgUnit { Code = "QAC", Kind = OrgUnitKind.Administration, Path = "/CU/QAC/" };
        var department = new OrgUnit { Code = "ENG-CIV", Kind = OrgUnitKind.Department, Path = "/CU/ENG/ENG-CIV/" };
        var collegeLevel = new CycleIndicator { Level = CollectionLevel.College, CentralUnitId = central.Id };
        var departmentLevel = new CycleIndicator { Level = CollectionLevel.Department, CentralUnitId = central.Id, CollegeEntry = true };
        var distinct = new IndicatorVersion { Aggregation = Aggregation.DistinctCount };

        Assert.True(FormsService.Applies(collegeLevel, College) && FormsService.Applies(collegeLevel, central));
        Assert.True(FormsService.Applies(departmentLevel, department) && FormsService.Applies(departmentLevel, College));
        Assert.Contains("الجامعة كلها", FormsService.EntryNote(collegeLevel, distinct, central));
        Assert.Contains("بين أقسامها", FormsService.EntryNote(departmentLevel, distinct, College));
        Assert.Null(FormsService.EntryNote(collegeLevel, distinct, College));
    }
}
