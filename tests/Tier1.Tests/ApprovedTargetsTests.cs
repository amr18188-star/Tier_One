using Tier1.Application.Kpi;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Tests;

/// <summary>
/// D3: الأداء يقاس بالمستهدف المعتمد وحده. المقترح لا يغير مقياس جهة قبل اعتماد اللجنة العليا، ويسري آخر معتمد قبله،
/// ويعرض المقترح «بانتظار الاعتماد» (PendingValue) دون أن يدخل أي حساب.
/// </summary>
public class ApprovedTargetsTests
{
    private static readonly OrgUnit University = new() { Code = "CU", Kind = OrgUnitKind.University, Path = "/CU/" };
    private static readonly OrgUnit College = new() { Code = "ENG", Kind = OrgUnitKind.College, Path = "/CU/ENG/", ParentId = University.Id };

    private static IndicatorTarget T(OrgUnit unit, int year, decimal value, TargetStatus status, TargetKind kind = TargetKind.Target) =>
        new() { UnitId = unit.Id, Year = year, Value = value, Kind = kind, Status = status };

    private static TargetInfo Lookup(List<IndicatorTarget> targets, OrgUnit unit, int year = 2027, Aggregation aggregation = Aggregation.RatioOfSums) =>
        TargetLookup.For(targets, unit, University.Id, year, planStart: null, aggregation);

    [Fact]
    public void A_proposed_target_for_a_later_year_does_not_replace_the_approved_one()
    {
        // العميد يقترح لسنة 2027 مستهدفًا أقل؛ يبقى معتمد 2026 ساريًا حتى تعتمد اللجنة.
        var targets = new List<IndicatorTarget> { T(College, 2026, 80, TargetStatus.Approved), T(College, 2027, 50, TargetStatus.Proposed) };
        var info = Lookup(targets, College);
        Assert.Equal(80m, info.Value);
        Assert.Equal(2026, info.Year);
        Assert.Equal(TargetSource.Unit, info.Source);
        Assert.Equal(50m, info.PendingValue);
        Assert.Equal(2027, info.PendingYear);
    }

    [Fact]
    public void A_proposed_college_target_does_not_override_the_approved_each_college_target()
    {
        var each = new List<IndicatorTarget> { T(College, 2027, 40, TargetStatus.Proposed), T(University, 2027, 70, TargetStatus.Approved, TargetKind.EachCollege) };
        var a = Lookup(each, College);
        Assert.Equal((70m, TargetSource.EachCollege), (a.Value!.Value, a.Source));
        Assert.Equal(40m, a.PendingValue);

        // ADR-0017: the university's approved target is not the college's measure; the college has none until its own is approved.
        var uni = new List<IndicatorTarget> { T(College, 2027, 40, TargetStatus.Proposed), T(University, 2027, 85, TargetStatus.Approved) };
        var b = Lookup(uni, College);
        Assert.Equal((null, TargetSource.None), (b.Value, b.Source));
        Assert.Equal(40m, b.PendingValue);
    }

    [Fact]
    public void With_only_a_proposed_target_there_is_no_target_but_it_shows_as_pending()
    {
        var info = Lookup([T(College, 2027, 60, TargetStatus.Proposed)], College);
        Assert.Null(info.Value);
        Assert.Equal(TargetSource.None, info.Source);
        Assert.True(info.HasPending);
        Assert.Equal(60m, info.PendingValue);
    }

    [Fact]
    public void Approving_the_proposal_makes_it_the_target_and_clears_pending()
    {
        var info = Lookup([T(College, 2026, 80, TargetStatus.Approved), T(College, 2027, 50, TargetStatus.Approved)], College);
        Assert.Equal(50m, info.Value);
        Assert.False(info.HasPending);
        Assert.Equal(info, info with { PendingValue = null, PendingYear = null });
    }

    [Fact]
    public void An_older_proposal_behind_a_newer_approved_target_is_not_pending()
    {
        // المقترح الأقدم لا يسري أصلًا ولو اعتمد، فلا يعرض «بانتظار الاعتماد».
        var info = Lookup([T(College, 2026, 50, TargetStatus.Proposed), T(College, 2027, 80, TargetStatus.Approved)], College);
        Assert.Equal(80m, info.Value);
        Assert.False(info.HasPending);
    }

    [Fact]
    public void The_university_target_used_for_comparison_is_the_approved_one()
    {
        var targets = new List<IndicatorTarget> { T(University, 2026, 90, TargetStatus.Approved), T(University, 2027, 60, TargetStatus.Proposed) };
        Assert.Equal(90m, TargetLookup.University(targets, University.Id, 2027, planStart: null));
        var own = TargetLookup.For(targets, University, University.Id, 2027, null, Aggregation.Sum);
        Assert.Equal((90m, 60m), (own.Value!.Value, own.PendingValue!.Value));
    }
}
