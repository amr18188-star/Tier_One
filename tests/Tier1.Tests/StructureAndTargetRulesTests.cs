using Tier1.Application.Kpi;
using Tier1.Application.Organization;
using Tier1.Application.Structure;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;
using Tier1.Domain.Structure;

namespace Tier1.Tests;

public class StructureRulesTests
{
    private static readonly StructurePosition Dean = new() { TitleAr = "عميد الكلية", Single = true, Required = true };
    private static readonly StructurePosition Rep = new() { TitleAr = "ممثل Tier 1 بالقسم", Single = true, Required = true };
    private static readonly StructurePosition Industry = new() { TitleAr = StructureRules.IndustryMemberTitle };

    private static readonly IReadOnlyList<StructureUnit> Template =
    [
        new() { Code = "DEAN", NameAr = "عميد الكلية", Positions = [Dean] },
        new() { Code = StructureRules.AdvisoryCouncilCode, NameAr = "المجلس الاستشاري الصناعي", Positions = [Industry] },
        new() { Code = "DEPARTMENTS", NameAr = "الأقسام العلمية", PerDepartment = true, Positions = [Rep] },
    ];

    private static readonly Guid Civil = Guid.NewGuid();
    private static readonly Guid Electrical = Guid.NewGuid();

    [Fact]
    public void An_empty_college_lists_every_gap()
    {
        var r = StructureRules.Evaluate(Template, [], [(Civil, "المدنية"), (Electrical, "الكهربية")], []);
        // العميد + عضو صناعي + ممثل لكل قسم (2) + شريك لكل قسم (2)
        Assert.Equal(6, r.Required);
        Assert.Equal(0, r.Percent);
        Assert.Contains(r.Issues, i => i.Kind == "no_industry_member");
        Assert.Equal(2, r.Issues.Count(i => i.Kind == "no_partner"));
    }

    [Fact]
    public void A_complete_college_is_100_percent()
    {
        StructureMember M(StructurePosition p, Guid? dept = null) => new() { PositionId = p.Id, DepartmentId = dept };
        var r = StructureRules.Evaluate(
            Template,
            [M(Dean), M(Industry), M(Rep, Civil), M(Rep, Electrical)],
            [(Civil, "المدنية"), (Electrical, "الكهربية")],
            [new IndustryPartner { DepartmentId = Civil }, new IndustryPartner { DepartmentId = Electrical }]);
        Assert.Equal(100, r.Percent);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void A_membership_is_active_between_its_dates()
    {
        var m = new StructureMember { StartDate = new(2026, 9, 1), EndDate = new(2026, 12, 31) };
        Assert.False(m.IsActiveOn(new(2026, 8, 31)));
        Assert.True(m.IsActiveOn(new(2026, 12, 31)));
        Assert.False(m.IsActiveOn(new(2027, 1, 1)));
    }
}

public class TargetRulesTests
{
    private static readonly OrgUnit University = new() { Code = "CU", Kind = OrgUnitKind.University, Path = "/CU/" };
    private static readonly OrgUnit College = new() { Code = "ENG", Kind = OrgUnitKind.College, Path = "/CU/ENG/" };
    private static readonly IndicatorVersion Percent = new() { Unit = "%" };
    private static readonly IndicatorVersion Count = new() { Unit = "عدد" };

    private static SaveTargetRequest Req(int year = 2027, TargetKind kind = TargetKind.Target, decimal? value = 50, decimal? num = null, decimal? den = null) =>
        new(Guid.Empty, year, kind, value, null, num, den, null, null, null, null, null);

    private static string Code(SaveTargetRequest r, OrgUnit u, IndicatorVersion v) => Assert.Throws<DomainException>(() => TargetRules.Validate(r, u, v)).Code;

    [Fact]
    public void A_valid_target_passes()
    {
        TargetRules.Validate(Req(), College, Percent);
        TargetRules.Validate(Req(2026, TargetKind.Baseline), University, Percent);
        TargetRules.Validate(Req(kind: TargetKind.EachCollege, value: 2), University, Count);
    }

    [Fact]
    public void The_ratio_is_computed_on_the_server_from_numerator_and_denominator()
    {
        // القيمة المرسلة تهمل: النتيجة المحسوبة لا تعدل يدويًا.
        Assert.Equal(62.5m, TargetRules.Validate(Req(value: 99, num: 125, den: 200), College, Percent));
        Assert.Equal(62.5m, TargetRules.Validate(Req(value: null, num: 125, den: 200), College, Percent));
        Assert.Equal(50m, TargetRules.Validate(Req(value: 50), College, Percent));
    }

    [Fact]
    public void The_messages_say_what_to_fix()
    {
        Assert.Equal("percent_range", Code(Req(value: 120), College, Percent));
        Assert.Equal("negative_value", Code(Req(value: -1), College, Count));
        Assert.Equal("invalid_year", Code(Req(2035), College, Percent));
        Assert.Equal("baseline_year", Code(Req(2027, TargetKind.Baseline), University, Percent));
        Assert.Equal("each_college_unit", Code(Req(kind: TargetKind.EachCollege), College, Count));
        Assert.Equal("numerator_denominator", Code(Req(num: 5), College, Percent));
        var e = Assert.Throws<DomainException>(() => TargetRules.Validate(Req(num: 520, den: 500), College, Percent));
        Assert.Equal("numerator_exceeds", e.Code);
        Assert.Contains("520", e.Message, StringComparison.Ordinal);
        Assert.Equal("value_required", Code(Req(value: null), College, Percent));
    }
}

public class OrgRulesTests
{
    [Theory]
    [InlineData(" eng-civ ", "ENG-CIV")]
    [InlineData("CS", "CS")]
    public void Codes_are_normalized(string input, string expected) => Assert.Equal(expected, OrgRules.NormalizeCode(input));

    [Theory]
    [InlineData("")]
    [InlineData("-ENG")]
    [InlineData("كلية")]
    [InlineData("ENG CIV")]
    public void Bad_codes_are_refused(string input) =>
        Assert.Equal("invalid_code", Assert.Throws<DomainException>(() => OrgRules.NormalizeCode(input)).Code);

    [Fact]
    public void A_department_belongs_to_a_college_and_a_college_to_the_university()
    {
        var university = new OrgUnit { Kind = OrgUnitKind.University };
        var college = new OrgUnit { Kind = OrgUnitKind.College };
        OrgRules.RequireParent(OrgUnitKind.College, university);
        OrgRules.RequireParent(OrgUnitKind.Department, college);
        Assert.Equal("invalid_parent", Assert.Throws<DomainException>(() => OrgRules.RequireParent(OrgUnitKind.Department, university)).Code);
        Assert.Equal("invalid_parent", Assert.Throws<DomainException>(() => OrgRules.RequireParent(OrgUnitKind.University, college)).Code);
        Assert.Equal("parent_inactive", Assert.Throws<DomainException>(() => OrgRules.RequireParent(OrgUnitKind.Department, new OrgUnit { Kind = OrgUnitKind.College, Active = false })).Code);
    }

    [Fact]
    public void A_scope_code_covers_its_unit_and_everything_below_only()
    {
        Assert.True(OrgScope.Covers(["ENG"], "/CU/ENG/"));
        Assert.True(OrgScope.Covers(["ENG"], "/CU/ENG/ENG-CIV/"));
        Assert.False(OrgScope.Covers(["ENG"], "/CU/ENGX/"));
        Assert.False(OrgScope.Covers(["ENG-CIV"], "/CU/ENG/"));
    }
}
