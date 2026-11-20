using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

/// <summary>ARCH-1: which indicators of a cycle belong in a unit's form, and what to enter for them (pure rules).</summary>
public static class FormAssignmentRules
{
    /// <summary>هل يدخل المؤشر في نموذج الجهة؟ بمستواه، وبالجهات المكلفة به عند فتح الدورة، أو لأنها جهة إدخاله المركزية.</summary>
    public static bool Applies(CycleIndicator ci, OrgUnit unit) => unit.Kind switch
    {
        OrgUnitKind.Department => ci.Level == CollectionLevel.Department && Tasked(ci, unit),
        OrgUnitKind.College or OrgUnitKind.Institute => ((ci.Level == CollectionLevel.College || ci.CollegeEntry) && Tasked(ci, unit)) || ci.CentralUnitId == unit.Id,
        _ => ci.CentralUnitId == unit.Id,
    };

    private static bool Tasked(CycleIndicator ci, OrgUnit unit) => ci.UnitIds is null || ci.UnitIds.Contains(unit.Id);

    /// <summary>ما يدخل في هذا النموذج تحديدًا حين لا يكون رقم الجهة وحدها: العدد بعد إزالة التكرار للكلية أو للجامعة كلها.</summary>
    public static string? EntryNote(CycleIndicator ci, IndicatorVersion v, OrgUnit unit)
    {
        if (v.Aggregation != Aggregation.DistinctCount) return null;
        if (!unit.IsCollegeLevel && unit.Kind != OrgUnitKind.Department)
            return "أدخل عدد الجامعة كلها بعد إزالة التكرار (ما تشترك فيه كليتان يعد مرة واحدة)، لا مجموع أعداد الكليات.";
        if (unit.IsCollegeLevel && ci.Level == CollectionLevel.Department)
            return "أدخل عدد الكلية كلها بعد إزالة التكرار بين أقسامها، لا مجموع أعداد الأقسام.";
        return null;
    }
}
