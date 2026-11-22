using Tier1.Application.Scorecard;
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

    private static string Code(SaveTargetRequest r, OrgUnit u, IndicatorVersion v) => Assert.Throws<DomainException>(() => TargetRules.Validate(r, u, v, PlanPeriod.Default)).Code;

    [Fact]
    public void A_valid_target_passes()
    {
        TargetRules.Validate(Req(), College, Percent, PlanPeriod.Default);
        TargetRules.Validate(Req(2026, TargetKind.Baseline), University, Percent, PlanPeriod.Default);
        TargetRules.Validate(Req(kind: TargetKind.EachCollege, value: 2), University, Count, PlanPeriod.Default);
    }

    [Fact]
    public void The_ratio_is_computed_on_the_server_from_numerator_and_denominator()
    {
        // القيمة المرسلة تهمل: النتيجة المحسوبة لا تعدل يدويًا.
        Assert.Equal(62.5m, TargetRules.Validate(Req(value: 99, num: 125, den: 200), College, Percent, PlanPeriod.Default));
        Assert.Equal(62.5m, TargetRules.Validate(Req(value: null, num: 125, den: 200), College, Percent, PlanPeriod.Default));
        Assert.Equal(50m, TargetRules.Validate(Req(value: 50), College, Percent, PlanPeriod.Default));
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
        var e = Assert.Throws<DomainException>(() => TargetRules.Validate(Req(num: 520, den: 500), College, Percent, PlanPeriod.Default));
        Assert.Equal("numerator_exceeds", e.Code);
        Assert.Contains("520", e.Message, StringComparison.Ordinal);
        Assert.Equal("value_required", Code(Req(value: null), College, Percent));
    }
}

public class OrgRulesTests
{
    /// <summary>
    /// The units list goes by depth, then the unit's order among its siblings, then its code. A sector whose code is longer than
    /// its siblings' (SE01 beside L02 and F01) keeps its place after the secretary general's sector and before the colleges; ordering
    /// by the path's text length put it after every college.
    /// </summary>
    [Fact]
    public void The_units_list_is_ordered_by_depth_then_sort_order_not_by_code_length()
    {
        static OrgUnit U(string code, string path, int order) => new() { Code = code, Path = path, SortOrder = order };
        var listed = OrgRules.InListOrder([
            U("F01", "/CU/F01/", 100), U("SPU", "/CU/SE01/SPU/", 1), U("SE01", "/CU/SE01/", 6), U("L02", "/CU/L02/", 5), U("CU", "/CU/", 0), U("A17", "/CU/L02/A17/", 17),
        ]).Select(u => u.Code);
        Assert.Equal(["CU", "L02", "SE01", "F01", "SPU", "A17"], listed);
    }

    /// <summary>
    /// The strategic entities (2026-11-23): a group of their own after the secretary general's sector and before the colleges, with the
    /// university's eighteen entities in the order given. The planning unit and the ICT centre moved into it and still see the whole university.
    /// </summary>
    [Fact]
    public void The_strategic_entities_come_after_the_secretary_general_and_before_the_colleges()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Tier1.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var file = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "src", "Tier1.Infrastructure", "Persistence", "Seed", "university-structure.json")));
        var units = file.RootElement.GetProperty("units").EnumerateArray()
            .Select(u => (Code: u.GetProperty("code").GetString()!, Name: u.GetProperty("nameAr").GetString()!, Kind: u.GetProperty("kind").GetString()!,
                Parent: u.GetProperty("parent").GetString(), Order: u.GetProperty("sortOrder").GetInt32(), Whole: u.GetProperty("viewsWholeUniversity").GetBoolean())).ToList();
        var group = units.Single(u => u.Name == "الكيانات الاستراتيجية");
        Assert.Equal(("Sector", (string?)null), (group.Kind, group.Parent));
        var top = units.Where(u => u.Parent is null).OrderBy(u => u.Order).Select(u => u.Name).ToList();
        Assert.Equal("قطاع أمين الجامعة", top[top.IndexOf(group.Name) - 1]);
        Assert.StartsWith("كلية", top[top.IndexOf(group.Name) + 1]);

        var entities = units.Where(u => u.Parent == group.Code).OrderBy(u => u.Order).ToList();
        Assert.Equal(18, entities.Count);
        Assert.Equal(["وحدة التخطيط الاستراتيجي", "وحدة ضمان الجودة", "وحدة إدارة المشروعات", "مركز الاتصالات وتكنولوجيا المعلومات"], entities.Take(4).Select(u => u.Name));
        Assert.Equal("مركز النانو تكنولوجي", entities[^1].Name);
        Assert.Equal(["ICT", "SPU"], entities.Where(u => u.Whole).Select(u => u.Code).Order());
    }

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
