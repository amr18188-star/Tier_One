namespace Tier1.Domain.Collection;

public enum ReopenRequestStatus
{
    /// <summary>ينتظر قرار اللجنة العليا؛ النموذج باق معتمدًا ومقفلًا.</summary>
    Pending = 1,

    /// <summary>اعتمدته اللجنة برقم المحضر فأعيد فتح النموذج، وحفظت قيمه المعتمدة في لقطة.</summary>
    Approved = 2,

    /// <summary>رفضته اللجنة بسبب؛ النموذج باق معتمدًا.</summary>
    Rejected = 3,
}

/// <summary>
/// طلب إعادة فتح نموذج معتمد (D4، ADR-0004): يقدمه من يملك «إعادة الفتح» بسبب، وتقرره اللجنة العليا برقم المحضر.
/// لا يفتح النموذج إلا باعتماده، ولا يقرره مقدمه.
/// </summary>
public sealed class ReopenRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SubmissionId { get; set; }

    public Guid RequestedBy { get; set; }

    public string? RequestedByName { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public string Reason { get; set; } = "";

    public ReopenRequestStatus Status { get; set; } = ReopenRequestStatus.Pending;

    public Guid? DecidedBy { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>رقم محضر اللجنة العليا؛ إلزامي للاعتماد.</summary>
    public string? DecisionRef { get; set; }

    /// <summary>ملاحظة القرار؛ إلزامية للرفض.</summary>
    public string? DecisionNote { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// القيمة كما اعتمدت قبل إعادة فتح النموذج (D4). تبقى القيمة الرسمية في النتائج حتى يعاد اعتماد النموذج،
/// وتحفظ القيمة السابقة ومرحلة اعتمادها كما يطلب المتطلب 4-6.
/// </summary>
public sealed class ValueSnapshot
{
    public long Id { get; set; }

    public Guid RequestId { get; set; }

    public Guid SubmissionId { get; set; }

    public Guid IndicatorId { get; set; }

    public ValueKind Kind { get; set; }

    public decimal? Value { get; set; }

    public decimal? Numerator { get; set; }

    public decimal? Denominator { get; set; }

    public string? Reason { get; set; }

    public string? Explanation { get; set; }

    public Guid EditedBy { get; set; }

    public DateTimeOffset EditedAt { get; set; }

    /// <summary>متى اعتمد النموذج الذي أخذت منه القيمة.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }
}
