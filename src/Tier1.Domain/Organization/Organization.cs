namespace Tier1.Domain.Organization;

public enum OrgUnitKind
{
    University = 1,
    College = 2,
    Institute = 3,
    Department = 4,
    Center = 5,
    Administration = 6,

    /// <summary>قطاع (نائب رئيس الجامعة أو أمينها): تتبعه إدارات عامة ومراكز.</summary>
    Sector = 7,
}

/// <summary>
/// جهة في هيكل الجامعة: الجامعة، كلية أو معهد، قسم علمي، مركز أو إدارة.
/// الرمز ثابت لا يتغير بعد الإنشاء، لأنه قيمة «نطاقه» في إسناد الأدوار وفي قوالب Excel.
/// المسار من رموز الأسلاف (<c>/CU/ENG/ENG-CIV/</c>) يجعل فحص النطاق مقارنة نصية بلا استعلام شجري.
/// </summary>
public sealed class OrgUnit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    public OrgUnitKind Kind { get; set; }

    public Guid? ParentId { get; set; }

    public string Path { get; set; } = "";

    /// <summary>الجهة المعطلة تبقى في السجلات السابقة ولا تظهر في النماذج الجديدة؛ لا تحذف أبدًا.</summary>
    public bool Active { get; set; } = true;

    public int SortOrder { get; set; }

    public DateOnly? EstablishedOn { get; set; }

    public DateOnly? ClosedOn { get; set; }

    /// <summary>عند الدمج: الجهة التي انتقل إليها عمل هذه الجهة.</summary>
    public Guid? MergedIntoId { get; set; }

    /// <summary>
    /// جهة دورها في Tier 1 على الجامعة كلها (وحدة التخطيط الاستراتيجي، مركز تكنولوجيا المعلومات):
    /// من نطاقه هذه الجهة يطلع (عرضًا وطباعة) على الجامعة كاملة، ويبقى الإدخال والتعديل في نماذج جهته وحدها.
    /// </summary>
    public bool ViewsWholeUniversity { get; set; }

    /// <summary>
    /// مؤشرات الجهة محددة بقائمة (<see cref="UnitIndicator"/>) بدل الافتراضي: كل مؤشرات مستواها للكليات والأقسام، ولا شيء للجهات المركزية.
    /// القسم غير المخصص يتبع قائمة كليته إن خصصت.
    /// </summary>
    public bool CustomIndicators { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }

    /// <summary>الكليات والمعاهد هي مستوى توزيع المستهدفات وهيكل Tier 1.</summary>
    public bool IsCollegeLevel => Kind is OrgUnitKind.College or OrgUnitKind.Institute;

    /// <summary>جهة مركزية: لا كلية ولا قسم ولا الجامعة نفسها.</summary>
    public bool IsCentral => Kind is OrgUnitKind.Sector or OrgUnitKind.Administration or OrgUnitKind.Center;

    public static string PathOf(string? parentPath, string code) => (string.IsNullOrEmpty(parentPath) ? "/" : parentPath) + code + "/";
}

/// <summary>تكليف جهة بمؤشر تدخل بياناته (حين تكون مؤشرات الجهة مخصصة).</summary>
public sealed class UnitIndicator
{
    public Guid UnitId { get; set; }

    public Guid IndicatorId { get; set; }
}

public enum OrgUnitChangeKind
{
    Created = 1,
    Renamed = 2,
    Moved = 3,
    Merged = 4,
    Closed = 5,
    Reactivated = 6,
}

/// <summary>تاريخ الجهة بتاريخ سريان: الإنشاء وتغيير الاسم والنقل والدمج والإغلاق.</summary>
public sealed class OrgUnitChange
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UnitId { get; set; }

    public OrgUnitChangeKind Kind { get; set; }

    public DateOnly EffectiveOn { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? Reason { get; set; }

    public Guid? By { get; set; }

    public DateTimeOffset At { get; set; }
}

/// <summary>
/// فحص النطاق على الهيكل: قيمة «نطاقه» رمز جهة، فتغطي الجهة نفسها وكل ما تحتها.
/// </summary>
public static class OrgScope
{
    public static bool Covers(IEnumerable<string> scopeCodes, string unitPath) =>
        scopeCodes.Any(code => unitPath.Contains("/" + code + "/", StringComparison.Ordinal));
}
