using Tier1.Domain.Structure;

namespace Tier1.Application.Structure;

public sealed record CompletenessIssue(string Kind, string MessageAr, Guid? DepartmentId = null, Guid? PositionId = null);

public sealed record Completeness(int Required, int Filled, IReadOnlyList<CompletenessIssue> Issues)
{
    public int Percent => Required == 0 ? 100 : (int)Math.Round(100m * Filled / Required);
}

/// <summary>
/// اكتمال هيكل Tier 1 في كلية (دالة نقية): كل منصب إلزامي شاغل، وكل قسم علمي له ممثل Tier 1 وشريك صناعي،
/// والمجلس الاستشاري الصناعي فيه عضو من الصناعة.
/// </summary>
public static class StructureRules
{
    public const string AdvisoryCouncilCode = "ADVISORY";
    public const string IndustryMemberTitle = "عضو من جهة صناعية";

    public static Completeness Evaluate(
        IReadOnlyList<StructureUnit> template,
        IReadOnlyList<StructureMember> activeMembers,
        IReadOnlyList<(Guid Id, string Name)> departments,
        IReadOnlyList<IndustryPartner> activePartners)
    {
        var issues = new List<CompletenessIssue>();
        var required = 0;
        var filled = 0;

        foreach (var unit in template)
        {
            foreach (var position in unit.Positions.Where(p => p.Required))
            {
                if (unit.PerDepartment)
                {
                    foreach (var (id, name) in departments)
                    {
                        required++;
                        if (activeMembers.Any(m => m.PositionId == position.Id && m.DepartmentId == id)) filled++;
                        else issues.Add(new("vacant_department_position", $"قسم {name}: لا يوجد {position.TitleAr}.", id, position.Id));
                    }
                }
                else
                {
                    required++;
                    if (activeMembers.Any(m => m.PositionId == position.Id)) filled++;
                    else issues.Add(new("vacant_position", $"{unit.NameAr}: منصب «{position.TitleAr}» شاغر.", null, position.Id));
                }
            }

            if (unit.Code == AdvisoryCouncilCode)
            {
                required++;
                var industry = unit.Positions.Where(p => p.TitleAr == IndustryMemberTitle).Select(p => p.Id).ToHashSet();
                if (activeMembers.Any(m => industry.Contains(m.PositionId))) filled++;
                else issues.Add(new("no_industry_member", "المجلس الاستشاري الصناعي بلا عضو من جهة صناعية."));
            }
        }

        foreach (var (id, name) in departments)
        {
            required++;
            if (activePartners.Any(p => p.DepartmentId == id)) filled++;
            else issues.Add(new("no_partner", $"قسم {name}: لا يوجد شريك صناعي.", id));
        }

        return new Completeness(required, filled, issues);
    }
}
