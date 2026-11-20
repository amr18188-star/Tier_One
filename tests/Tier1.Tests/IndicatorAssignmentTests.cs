using Tier1.Application.Access;
using Tier1.Application.Collection;
using Tier1.Application.Organization;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Tests;

public class IndicatorAssignmentTests
{
    private static readonly OrgUnit Root = new() { Code = "CU", Kind = OrgUnitKind.University, Path = "/CU/" };
    private static readonly OrgUnit Eng = Unit("ENG", OrgUnitKind.College, Root);
    private static readonly OrgUnit Civ = Unit("ENG-CIV", OrgUnitKind.Department, Eng);
    private static readonly OrgUnit Com = Unit("COM", OrgUnitKind.College, Root);
    private static readonly OrgUnit Acc = Unit("COM-ACC", OrgUnitKind.Department, Com);
    private static readonly OrgUnit Spu = Unit("SPU", OrgUnitKind.Administration, Root);

    private static readonly IndicatorRef CollegeRatio = new(Guid.NewGuid(), CollectionLevel.College, false);
    private static readonly IndicatorRef DeptRatio = new(Guid.NewGuid(), CollectionLevel.Department, false);
    private static readonly IndicatorRef UniCentral = new(Guid.NewGuid(), CollectionLevel.University, false);

    private static OrgUnit Unit(string code, OrgUnitKind kind, OrgUnit parent) =>
        new() { Code = code, Kind = kind, ParentId = parent.Id, Path = OrgUnit.PathOf(parent.Path, code) };

    private static AssignmentMap Map(params (OrgUnit Unit, IndicatorRef[] Indicators)[] custom)
    {
        var units = new[] { Root, Eng, Civ, Com, Acc, Spu }.Select(u => new OrgUnit
        {
            Id = u.Id, Code = u.Code, Kind = u.Kind, ParentId = u.ParentId, Path = u.Path,
            CustomIndicators = custom.Any(c => c.Unit.Id == u.Id),
        }).ToList();
        var rows = custom.SelectMany(c => c.Indicators.Select(i => new UnitIndicator { UnitId = c.Unit.Id, IndicatorId = i.Id }));
        return new AssignmentMap(units, rows);
    }

    [Fact]
    public void Without_customisation_colleges_and_departments_enter_everything_of_their_level()
    {
        var map = Map();
        Assert.True(map.Enters(Eng, CollegeRatio));
        Assert.False(map.Enters(Eng, DeptRatio));
        Assert.True(map.Enters(Civ, DeptRatio));
        Assert.False(map.Enters(Spu, CollegeRatio));
        Assert.Null(map.ResponsibleFor(UniCentral.Id));
    }

    [Fact]
    public void A_customised_college_enters_only_its_list_and_its_departments_follow_it()
    {
        var map = Map((Eng, [DeptRatio]));
        Assert.False(map.Enters(Eng, CollegeRatio));
        Assert.True(map.Enters(Civ, DeptRatio));
        Assert.Equal(Eng.Code, map.FollowedCollege(Civ)?.Code);
        Assert.True(map.CountsFor(Eng, DeptRatio));
        Assert.False(map.CountsFor(Eng, CollegeRatio));
        // كلية أخرى غير مخصصة لا تتأثر.
        Assert.True(map.Enters(Com, CollegeRatio));
    }

    [Fact]
    public void A_customised_department_overrides_its_college()
    {
        var map = Map((Eng, [DeptRatio]), (Civ, []));
        Assert.False(map.Enters(Civ, DeptRatio));
        Assert.Null(map.FollowedCollege(Civ));
    }

    [Fact]
    public void University_level_indicators_go_to_the_assigned_central_unit()
    {
        var map = Map((Spu, [UniCentral]));
        Assert.Equal(Spu.Id, map.ResponsibleFor(UniCentral.Id)?.Id);
        Assert.True(map.Selected(Spu, UniCentral));
        Assert.False(map.Selected(Eng, UniCentral));
        Assert.False(AssignmentMap.Assignable(OrgUnitKind.Administration, CollectionLevel.College));
        Assert.True(AssignmentMap.Assignable(OrgUnitKind.College, CollectionLevel.University));
        Assert.False(AssignmentMap.Assignable(OrgUnitKind.Department, CollectionLevel.College));
    }

    [Fact]
    public void Forms_follow_the_units_tasked_when_the_cycle_opened()
    {
        var ci = new CycleIndicator { Level = CollectionLevel.College, UnitIds = [Com.Id] };
        Assert.True(FormAssignmentRules.Applies(ci, Com));
        Assert.False(FormAssignmentRules.Applies(ci, Eng));
        // دورة فتحت قبل التكليف (بلا قائمة): كل كليات المستوى.
        Assert.True(FormAssignmentRules.Applies(new CycleIndicator { Level = CollectionLevel.College }, Eng));
        var central = new CycleIndicator { Level = CollectionLevel.University, CentralUnitId = Spu.Id };
        Assert.True(FormAssignmentRules.Applies(central, Spu));
        Assert.False(FormAssignmentRules.Applies(central, Eng));
    }

    [Fact]
    public void Whole_university_units_widen_view_and_print_only()
    {
        var now = DateTimeOffset.UtcNow;
        var role = new AssignedRole("DE", false,
            [new RoleGrant { Feature = "dc.forms", Caps = PermissionCatalog.ParseCaps("VPE"), Scope = AccessScope.Unit }],
            new UserRole { UserId = Guid.NewGuid(), ValidFrom = now.AddDays(-1), ScopeValues = ["SPU"], ApprovedAt = now.AddDays(-1) });
        var a = EffectiveAccessCalculator.Compute(role.Assignment.UserId, [role], [], now, ["SPU", "ICT"]);
        Assert.True(UnitScope.For(a, "dc.forms", Cap.View).Covers("/CU/F06/"));
        Assert.True(UnitScope.For(a, "dc.forms", Cap.Print).All);
        var edit = UnitScope.For(a, "dc.forms", Cap.Edit);
        Assert.False(edit.Covers("/CU/F06/"));
        Assert.True(edit.Covers("/CU/SPU/"));

        // جهة عادية: يبقى نطاقه جهته.
        var plain = EffectiveAccessCalculator.Compute(role.Assignment.UserId, [role], [], now, ["ICT"]);
        Assert.False(UnitScope.For(plain, "dc.forms", Cap.View).Covers("/CU/F06/"));
    }
}
