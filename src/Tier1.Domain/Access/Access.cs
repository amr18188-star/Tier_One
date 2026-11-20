namespace Tier1.Domain.Access;

/// <summary>الإمكانيات الخمس كقناع بتي: عرض، طباعة، تعديل، إضافة، حذف.</summary>
[Flags]
#pragma warning disable CA1711 // الاسم مصطلح المنظومة نفسه
public enum Cap
#pragma warning restore CA1711
{
    None = 0,
    View = 1,
    Print = 2,
    Edit = 4,
    Add = 8,
    Delete = 16,
}

/// <summary>النطاق؛ الترتيب من الأضيق إلى الأوسع، فيؤخذ الأوسع عند جمع الأدوار.</summary>
public enum AccessScope
{
    /// <summary>سجلاته: ما أنشأه بنفسه.</summary>
    Own = 1,

    /// <summary>المسند إليه شخصيًا.</summary>
    Assigned = 2,

    /// <summary>نطاقه: الوحدات أو الخدمات المحددة عند إسناد الدور.</summary>
    Unit = 3,

    /// <summary>الكل.</summary>
    All = 4,
}

public enum OverrideEffect
{
    Allow = 1,
    Deny = 2,
}

public enum RbacChangeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,

    /// <summary>تغير الدور بعد الطلب، فلم يعد الفرق صالحًا للتطبيق.</summary>
    Stale = 5,
}

public sealed class Role
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>رمز ثابت مثل SA وDIR، أو رمز يختاره المدير لدور مخصص.</summary>
    public string Code { get; set; } = "";

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    public string? DescriptionAr { get; set; }

    /// <summary>دور من الكتالوج (لا يحذف ولا يغير رمزه).</summary>
    public bool IsSystem { get; set; }

    /// <summary>المدير الأعلى: يملك كل شيء ولا يعدل من المصفوفة.</summary>
    public bool IsLocked { get; set; }

    /// <summary>يزيد مع كل تعديل معتمد للمنح؛ طلب التعديل يحمل الرقم الذي بني عليه.</summary>
    public int GrantsVersion { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public List<RoleGrant> Grants { get; set; } = [];
}

/// <summary>منح دور لخاصية: الإمكانيات والنطاق والإجراءات الخاصة.</summary>
public sealed class RoleGrant
{
    public Guid RoleId { get; set; }

    public string Feature { get; set; } = "";

    public Cap Caps { get; set; }

    public AccessScope Scope { get; set; } = AccessScope.All;

    public List<string> Specials { get; set; } = [];
}

/// <summary>إسناد دور لمستخدم بنطاق ومدة. السحب يحفظ ولا يحذف.</summary>
public sealed class UserRole
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    /// <summary>قيم نطاق «نطاقه»: رموز وحدات أو خدمات أو مجموعات.</summary>
    public List<string> ScopeValues { get; set; } = [];

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public string? RevokeReason { get; set; }

    /// <summary>
    /// إسناد دور المدير الأعلى لا يسري حتى يعتمده مدير أعلى غير الذي أسنده (SOD-10)؛ بقية الأدوار تعتمد عند الإسناد.
    /// </summary>
    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public bool IsPendingApproval => ApprovedAt is null && RevokedAt is null;

    /// <summary>xmin: اعتماد إسناد وسحبه في اللحظة نفسها لا ينجحان معًا.</summary>
    public uint Version { get; set; }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ApprovedAt is not null && ValidFrom <= now && (ValidTo is null || ValidTo > now);
}

/// <summary>منح أو منع فردي لخاصية؛ المنع يغلب المنح دائمًا.</summary>
public sealed class UserOverride
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string Feature { get; set; } = "";

    public Cap Caps { get; set; }

    public List<string> Specials { get; set; } = [];

    public OverrideEffect Effect { get; set; }

    public AccessScope Scope { get; set; } = AccessScope.All;

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public string Reason { get; set; } = "";

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ValidFrom <= now && (ValidTo is null || ValidTo > now);
}

/// <summary>طلب تعديل صلاحيات دور ينتظر اعتماد مدير أعلى آخر (SOD-10).</summary>
public sealed class RbacChange
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RoleId { get; set; }

    public Guid RequestedBy { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public string Reason { get; set; } = "";

    /// <summary>رقم إصدار منح الدور الذي بني عليه الطلب.</summary>
    public int BaseVersion { get; set; }

    /// <summary>المنح الكاملة المقترحة (JSON).</summary>
    public string ProposedJson { get; set; } = "[]";

    /// <summary>الفرق عن المنح الحالية وقت الطلب (JSON) للعرض والسجل.</summary>
    public string DiffJson { get; set; } = "[]";

    public RbacChangeStatus Status { get; set; } = RbacChangeStatus.Pending;

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    /// <summary>xmin: اعتماد ورفض متزامنان لا ينجحان معًا.</summary>
    public uint Version { get; set; }
}

/// <summary>صف واحد يحمل رقم إصدار الصلاحيات العام؛ أي تعديل يرفعه فتعاد قراءة الصلاحيات الفعلية.</summary>
public sealed class AccessState
{
    public int Id { get; set; } = 1;

    public long Version { get; set; } = 1;

    /// <summary>The global IP allowlist is enforced (with its entries in iam.ip_allow_rules where user_id is null).</summary>
    public bool GlobalIpRestricted { get; set; }
}
