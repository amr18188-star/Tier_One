using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Organization;

/// <summary>مؤشر كما يحتاجه حساب التكليف: مستوى جمعه وهل هو عدد مميز (تدخله الكلية بعد إزالة التكرار بين أقسامها).</summary>
public sealed record IndicatorRef(Guid Id, CollectionLevel Level, bool DistinctCount);

/// <summary>
/// تكليف الجهات بالمؤشرات (دالة نقية):
/// <list type="bullet">
/// <item>الكلية أو المعهد غير المخصص يدخل كل مؤشرات الكليات، وأقسامه كل مؤشرات الأقسام. المخصص يدخل ما في قائمته فقط،
/// وقائمة الكلية تشمل مؤشرات الأقسام التي تدخلها أقسامها.</item>
/// <item>القسم غير المخصص يتبع قائمة كليته إن خصصت، وإلا يدخل كل مؤشرات الأقسام.</item>
/// <item>مؤشر الجامعة كلها تدخله جهة واحدة نيابة عن الجامعة: الجهة المكلفة به (مركزية أو كلية)، وإلا الجهة المالكة في التعريف، وإلا نموذج الجامعة.</item>
/// <item>الجهة المركزية لا تدخل إلا ما كلفت به من مؤشرات الجامعة.</item>
/// </list>
/// </summary>
public sealed class AssignmentMap
{
    private readonly Dictionary<Guid, OrgUnit> _units;
    private readonly Dictionary<Guid, HashSet<Guid>> _custom;

    public AssignmentMap(IEnumerable<OrgUnit> units, IEnumerable<UnitIndicator> rows)
    {
        _units = units.ToDictionary(u => u.Id);
        _custom = _units.Values.Where(u => u.CustomIndicators).ToDictionary(u => u.Id, _ => new HashSet<Guid>());
        foreach (var r in rows)
            if (_custom.TryGetValue(r.UnitId, out var set)) set.Add(r.IndicatorId);
    }

    /// <summary>هل يجوز تكليف هذا النوع من الجهات بمؤشر هذا المستوى؟</summary>
    public static bool Assignable(OrgUnitKind kind, CollectionLevel level) => kind switch
    {
        OrgUnitKind.Department => level == CollectionLevel.Department,
        // الكلية: مؤشراتها، ومؤشرات أقسامها، أو رقم الجامعة كلها نيابة عنها (كلية الدراسات العليا لمؤشرات البحث مثلًا).
        OrgUnitKind.College or OrgUnitKind.Institute => true,
        OrgUnitKind.Sector or OrgUnitKind.Administration or OrgUnitKind.Center => level == CollectionLevel.University,
        _ => false,
    };

    public bool IsCustom(Guid unitId) => _custom.ContainsKey(unitId);

    /// <summary>الكلية التي يتبع القسم قائمتها، إن كان القسم غير مخصص وكليته مخصصة.</summary>
    public OrgUnit? FollowedCollege(OrgUnit unit) =>
        unit.Kind == OrgUnitKind.Department && !_custom.ContainsKey(unit.Id) && unit.ParentId is { } p && _custom.ContainsKey(p) ? _units.GetValueOrDefault(p) : null;

    /// <summary>هل تدخل الكلية أو القسم قيمة المؤشر في نموذجها؟ (مؤشرات الجامعة كلها لها <see cref="ResponsibleFor"/>.)</summary>
    public bool Enters(OrgUnit unit, IndicatorRef i)
    {
        switch (unit.Kind)
        {
            case OrgUnitKind.Department:
                if (i.Level != CollectionLevel.Department) return false;
                if (_custom.TryGetValue(unit.Id, out var own)) return own.Contains(i.Id);
                return unit.ParentId is not { } parent || !_custom.TryGetValue(parent, out var college) || college.Contains(i.Id);
            case OrgUnitKind.College or OrgUnitKind.Institute:
                var applies = i.Level == CollectionLevel.College || (i.Level == CollectionLevel.Department && i.DistinctCount);
                return applies && (!_custom.TryGetValue(unit.Id, out var mine) || mine.Contains(i.Id));
            default:
                return false;
        }
    }

    /// <summary>هل للمؤشر نصيب في رقم الكلية (تدخله هي أو أقسامها)؟ لعرض تقدمها.</summary>
    public bool CountsFor(OrgUnit college, IndicatorRef i) =>
        i.Level != CollectionLevel.University && (!_custom.TryGetValue(college.Id, out var set) || set.Contains(i.Id));

    /// <summary>الجهة المكلفة بإدخال مؤشر على مستوى الجامعة كلها نيابة عنها، إن وجدت.</summary>
    public OrgUnit? ResponsibleFor(Guid indicatorId) =>
        _custom.Where(kv => kv.Value.Contains(indicatorId) && _units.TryGetValue(kv.Key, out var u) && u.Active && u.Kind != OrgUnitKind.Department)
            .Select(kv => _units[kv.Key]).OrderBy(u => u.Path, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>ما يظهر مختارًا في قائمة مؤشرات الجهة: ما تدخله فعلًا (أو تدخله أقسامها للكلية).</summary>
    public bool Selected(OrgUnit unit, IndicatorRef i) => unit.Kind switch
    {
        OrgUnitKind.Department => Enters(unit, i),
        OrgUnitKind.College or OrgUnitKind.Institute => i.Level == CollectionLevel.University
            ? _custom.TryGetValue(unit.Id, out var set) && set.Contains(i.Id)
            : CountsFor(unit, i),
        OrgUnitKind.University => false,
        _ => i.Level == CollectionLevel.University && _custom.TryGetValue(unit.Id, out var c) && c.Contains(i.Id),
    };
}
