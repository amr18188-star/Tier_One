namespace Tier1.Domain.Structure;

/// <summary>
/// وحدة في الهيكل المرجعي لـ Tier 1 على مستوى الكلية (لجنة المتابعة، المجلس الاستشاري الصناعي، ...).
/// يعرّف مرة واحدة للجامعة، وتبنى منه هياكل الكليات كلها.
/// </summary>
public sealed class StructureUnit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    public string NameAr { get; set; } = "";

    /// <summary>الدور المختصر كما في الرسم: «التخطيط والتنفيذ ومتابعة المؤشرات».</summary>
    public string RoleAr { get; set; } = "";

    public List<string> Duties { get; set; } = [];

    /// <summary>العميد رأس الهيكل؛ بقية الوحدات تتبعه مباشرة (قرار 2026-11-10).</summary>
    public bool IsHead { get; set; }

    /// <summary>وحدة تتكرر لكل قسم علمي (ممثل Tier 1 والشريك الصناعي).</summary>
    public bool PerDepartment { get; set; }

    public int SortOrder { get; set; }

    public List<StructurePosition> Positions { get; set; } = [];
}

/// <summary>منصب في وحدة: رئيس اللجنة، مقررها، عضو، ممثل Tier 1 بالقسم...</summary>
public sealed class StructurePosition
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UnitId { get; set; }

    public string TitleAr { get; set; } = "";

    /// <summary>منصب لا يتكرر (رئيس، مدير، عميد): شاغل واحد في الوقت نفسه.</summary>
    public bool Single { get; set; }

    /// <summary>خلوه يجعل هيكل الكلية غير مكتمل في لوحة الاكتمال.</summary>
    public bool Required { get; set; }

    /// <summary>الدور الذي يقترح لصاحب المنصب في المنظومة؛ لا يمنح إلا باعتماد.</summary>
    public string? SuggestedRoleCode { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// عضوية شخص في منصب بهيكل كلية. تغيير العضو يغلق عضويته بتاريخ ولا يحذفها، فيمكن عرض الهيكل كما كان في أي تاريخ.
/// </summary>
public sealed class StructureMember
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CollegeId { get; set; }

    /// <summary>للمناصب التي تتكرر لكل قسم.</summary>
    public Guid? DepartmentId { get; set; }

    public Guid PositionId { get; set; }

    public string FullName { get; set; } = "";

    /// <summary>الدرجة أو الصفة الوظيفية: «أستاذ — وكيل الكلية للدراسات العليا».</summary>
    public string? Title { get; set; }

    /// <summary>جهة العمل للأعضاء من خارج الجامعة (ممثلو الصناعة).</summary>
    public string? Employer { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? EndReason { get; set; }

    /// <summary>رقم قرار التشكيل وتاريخه كما ورد.</summary>
    public string? DecisionRef { get; set; }

    public string? Notes { get; set; }

    /// <summary>حساب العضو في المنظومة إن وجد.</summary>
    public Guid? UserId { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }

    public bool IsActiveOn(DateOnly day) => StartDate <= day && (EndDate is null || EndDate >= day);
}

/// <summary>الشريك الصناعي لقسم علمي.</summary>
public sealed class IndustryPartner
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CollegeId { get; set; }

    public Guid DepartmentId { get; set; }

    public string NameAr { get; set; } = "";

    public string? Sector { get; set; }

    public string? CoordinatorName { get; set; }

    public string? CoordinatorEmail { get; set; }

    public string? CoordinatorPhone { get; set; }

    public DateOnly? AgreementDate { get; set; }

    public string? AgreementRef { get; set; }

    public DateOnly? EndedOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }

    public bool IsActiveOn(DateOnly day) => EndedOn is null || EndedOn >= day;
}
