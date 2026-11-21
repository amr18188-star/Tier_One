namespace Tier1.Domain.Kpi;

public enum InitiativeStatus
{
    Planned = 1,
    InProgress = 2,
    Completed = 3,
    Stopped = 4,
}

/// <summary>
/// مبادرة تحت معيار: ما تنفذه الجامعة لتحقيق مستهدفات المعيار، بمدتها والجهة المسؤولة عنها والمؤشرات التي تؤثر فيها.
/// الرمز ثابت بعد الإنشاء (S04-M01 مثلًا).
/// </summary>
public sealed class Initiative
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    public Guid StandardId { get; set; }

    public string NameAr { get; set; } = "";

    /// <summary>شرح المبادرة وأنشطتها الرئيسة.</summary>
    public string? Description { get; set; }

    /// <summary>المخرجات أو الأثر المتوقع.</summary>
    public string? ExpectedOutcome { get; set; }

    public Guid? OwnerUnitId { get; set; }

    public int StartYear { get; set; }

    public int EndYear { get; set; }

    public InitiativeStatus Status { get; set; } = InitiativeStatus.Planned;

    /// <summary>رموز المؤشرات التي تسهم المبادرة في تحقيق مستهدفاتها.</summary>
    public List<string> IndicatorCodes { get; set; } = [];

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// نشاط من أنشطة مبادرة (ADR-0017): كل مبادرة أنشطتها الخاصة بها، بمسؤوله ومدته وحالته ونسبة إنجازه.
/// يحذف مع مبادرته. الحالات نفسها حالات المبادرة.
/// </summary>
public sealed class InitiativeActivity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid InitiativeId { get; set; }

    public string TitleAr { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>الجهة المسؤولة عن تنفيذ النشاط، إن حددت.</summary>
    public Guid? OwnerUnitId { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public InitiativeStatus Status { get; set; } = InitiativeStatus.Planned;

    /// <summary>نسبة الإنجاز من 0 إلى 100؛ النشاط المكتمل 100.</summary>
    public int Progress { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
