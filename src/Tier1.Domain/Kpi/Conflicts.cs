namespace Tier1.Domain.Kpi;

public enum ConflictSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum ConflictStatus
{
    Open = 1,

    /// <summary>جهزه مدير متابعة الخطة بالبدائل للعرض على اللجنة العليا.</summary>
    Ready = 2,

    Resolved = 3,

    /// <summary>أجّلت اللجنة الحسم؛ يعود إلى جدول أعمال لاحق.</summary>
    Deferred = 4,
}

/// <summary>
/// مشكلة في البيانات المرجعية (تعارض مستهدفات، وحدة غير مناسبة، رقم تالف...). تحسمها اللجنة العليا لـ Tier 1
/// ويحفظ قرارها برقم المحضر. القرار لا يغير الأرقام تلقائيًا؛ يطبقه مدير متابعة الخطة إصدارًا جديدًا.
/// </summary>
public sealed class DataConflict
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    /// <summary>أكواد المؤشرات أو المعايير المتأثرة كما في السجل («S04-09;S05-03» أو «S08»).</summary>
    public List<string> Scope { get; set; } = [];

    public string Type { get; set; } = "";

    public ConflictSeverity Severity { get; set; }

    public string Description { get; set; } = "";

    public string? ValueA { get; set; }

    public string? ValueB { get; set; }

    public string? Proposal { get; set; }

    public ConflictStatus Status { get; set; } = ConflictStatus.Open;

    public string? PreparationNote { get; set; }

    public Guid? PreparedBy { get; set; }

    public DateTimeOffset? PreparedAt { get; set; }

    public string? Decision { get; set; }

    public string DecidingBody { get; set; } = "اللجنة العليا لـ Tier 1";

    public DateOnly? DecisionDate { get; set; }

    public string? MinutesRef { get; set; }

    public Guid? DecisionRecordedBy { get; set; }

    public DateTimeOffset? DecisionRecordedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }

    public bool IsOpen => Status is ConflictStatus.Open or ConflictStatus.Ready or ConflictStatus.Deferred;
}
