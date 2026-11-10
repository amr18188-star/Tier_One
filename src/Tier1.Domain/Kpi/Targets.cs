namespace Tier1.Domain.Kpi;

public enum TargetKind
{
    Baseline = 1,
    Target = 2,

    /// <summary>
    /// مستهدف موحد «لكل كلية» كما ورد في ملف الكليات (مشروعان لكل كلية...)، يسري على كل كلية ليس لها مستهدف خاص.
    /// يحفظ على الجامعة لأنه لا يخص كلية بعينها.
    /// </summary>
    EachCollege = 3,
}

public enum TargetStatus
{
    Proposed = 1,
    Approved = 2,
}

/// <summary>
/// خط أساس أو مستهدف لمؤشر وجهة وسنة. القيمة رقمية، أو نص حين ورد المستهدف وصفيًا ولم يحسم رقمه بعد.
/// البسط والمقام يسجلان حين يحتاج توزيع النسبة إليهما.
/// </summary>
public sealed class IndicatorTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid IndicatorId { get; set; }

    public Guid UnitId { get; set; }

    public int Year { get; set; }

    public TargetKind Kind { get; set; }

    public decimal? Value { get; set; }

    public string? Text { get; set; }

    public decimal? Numerator { get; set; }

    public decimal? Denominator { get; set; }

    public string? Source { get; set; }

    public string? Note { get; set; }

    public TargetStatus Status { get; set; } = TargetStatus.Proposed;

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? DecisionRef { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>تعديل مستهدف معتمد: القيمة السابقة والجديدة والسبب ورقم القرار، للمقارنة والمساءلة.</summary>
public sealed class TargetRevision
{
    public long Id { get; set; }

    public Guid TargetId { get; set; }

    public decimal? OldValue { get; set; }

    public string? OldText { get; set; }

    public decimal? NewValue { get; set; }

    public string? NewText { get; set; }

    public string Reason { get; set; } = "";

    public string? DecisionRef { get; set; }

    public Guid ChangedBy { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
