using Tier1.Domain.Kpi;

namespace Tier1.Domain.Collection;

public enum CycleStatus
{
    /// <summary>قيد الإعداد: يحدد اسمها وسنتها ومواعيدها، ولا يراها مدخلو البيانات.</summary>
    Draft = 1,

    /// <summary>مفتوحة: نماذج الجهات متاحة للإدخال والمراجعة.</summary>
    Open = 2,

    /// <summary>مغلقة: لا إدخال ولا اعتماد؛ نتائجها المعتمدة ثابتة للمقارنة.</summary>
    Closed = 3,
}

/// <summary>
/// دورة قياس: فترة محددة تجمع فيها قيم المؤشرات من الجهات وتعتمد ثم يصدر تقرير الجامعة. تتكرر في السنة،
/// وتقارن نتائجها بمستهدف سنة محددة وبالدورة السابقة.
/// </summary>
public sealed class CollectionCycle
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = "";

    /// <summary>الفترة التي تقيسها الدورة كما تكتب في النماذج: «العام الأكاديمي 2025/2026».</summary>
    public string PeriodLabel { get; set; } = "";

    /// <summary>سنة المستهدف الذي تقارن به النتائج.</summary>
    public int TargetYear { get; set; }

    public DateOnly OpensOn { get; set; }

    public DateOnly DueOn { get; set; }

    public CycleStatus Status { get; set; } = CycleStatus.Draft;

    /// <summary>دورة تجريبية: تقبل تعريفات لم تعتمد بعد، وتظهر نتائجها موسومة بذلك.</summary>
    public bool Trial { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>مؤشر في الدورة بالتعريف الذي يقاس به (لقطة عند الفتح)؛ تعديل التعريف لاحقًا لا يغير ما قيس.</summary>
public sealed class CycleIndicator
{
    public Guid CycleId { get; set; }

    public Guid IndicatorId { get; set; }

    public Guid VersionId { get; set; }

    public string Code { get; set; } = "";

    public CollectionLevel Level { get; set; }

    /// <summary>
    /// جهة الإدخال المركزية: للمؤشرات على مستوى الجامعة كلها، وللعدد المميز الذي لا يجمع من الكليات (يدخل عدد الجامعة بعد إزالة التكرار).
    /// الجهة المالكة أو الجامعة.
    /// </summary>
    public Guid? CentralUnitId { get; set; }

    /// <summary>عدد مميز على مستوى القسم: تدخل الكلية أيضًا عددها بعد إزالة التكرار بين أقسامها، لأن أعداد الأقسام لا تجمع.</summary>
    public bool CollegeEntry { get; set; }
}

/// <summary>خطوات مسار الاعتماد بترتيبها؛ كل جهة تمر بالخطوات التي تخص نوعها فقط.</summary>
public enum ApprovalStep
{
    Entry = 1,
    DepartmentReview = 2,
    CollegeCommittee = 3,
    Dean = 4,
    Quality = 5,
    Done = 6,
}

public enum SubmissionStatus
{
    NotStarted = 1,
    InProgress = 2,

    /// <summary>أرسل وينتظر مراجعة الخطوة الحالية.</summary>
    InReview = 3,

    /// <summary>أعيد للتصحيح بملاحظات؛ يعود للإدخال.</summary>
    Returned = 4,

    /// <summary>اعتمد نهائيًا وقفل؛ لا يعدل إلا بإعادة فتح مسببة.</summary>
    Approved = 5,
}

/// <summary>نموذج جهة في دورة: كل المؤشرات المطلوبة منها، ويمر بمسار الاعتماد كوحدة واحدة.</summary>
public sealed class Submission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CycleId { get; set; }

    public Guid UnitId { get; set; }

    public ApprovalStep Step { get; set; } = ApprovalStep.Entry;

    public SubmissionStatus Status { get; set; } = SubmissionStatus.NotStarted;

    public DateOnly DueOn { get; set; }

    public Guid? LastEditedBy { get; set; }

    public DateTimeOffset? LastEditedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ReturnNote { get; set; }

    public uint Version { get; set; }

    public bool IsEditable => Step == ApprovalStep.Entry && Status is SubmissionStatus.NotStarted or SubmissionStatus.InProgress or SubmissionStatus.Returned;
}

/// <summary>
/// حالة القيمة: رقم (والصفر رقم)، أو غير متاح، أو غير منطبق بسبب مكتوب. الحقل الفارغ لا صف له، ولا يتحول إلى صفر.
/// </summary>
public enum ValueKind
{
    Number = 1,
    NotAvailable = 2,
    NotApplicable = 3,
}

/// <summary>قيمة مؤشر في نموذج جهة.</summary>
public sealed class SubmittedValue
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SubmissionId { get; set; }

    public Guid IndicatorId { get; set; }

    public ValueKind Kind { get; set; }

    /// <summary>القيمة؛ للنسبة تحسب في الخادم من البسط والمقام ولا تعدل يدويًا.</summary>
    public decimal? Value { get; set; }

    public decimal? Numerator { get; set; }

    public decimal? Denominator { get; set; }

    /// <summary>سبب «غير متاح» أو «غير منطبق».</summary>
    public string? Reason { get; set; }

    /// <summary>تفسير التحذير (اختلاف كبير عن آخر قيمة معتمدة).</summary>
    public string? Explanation { get; set; }

    /// <summary>ملاحظة المراجع عند الإعادة؛ تبرز الحقل لمدخل البيانات.</summary>
    public string? ReviewNote { get; set; }

    public Guid EditedBy { get; set; }

    public DateTimeOffset EditedAt { get; set; }

    public uint Version { get; set; }
}

public enum SubmissionAction
{
    Submitted = 1,
    Approved = 2,
    Returned = 3,
    Reopened = 4,
}

/// <summary>سجل مسار النموذج: من أرسل أو اعتمد أو أعاد، ومتى، وبأي ملاحظة.</summary>
public sealed class SubmissionEvent
{
    public long Id { get; set; }

    public Guid SubmissionId { get; set; }

    public SubmissionAction Action { get; set; }

    public ApprovalStep FromStep { get; set; }

    public ApprovalStep ToStep { get; set; }

    public Guid By { get; set; }

    public string? ByName { get; set; }

    public DateTimeOffset At { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// مستند مرفوع: دليل لقيمة مؤشر، أو قرار تشكيل وصورة الهيكل المعتمد لكلية. لا يحذف نهائيًا: يبطل بسبب ويبقى في السجل.
/// </summary>
public sealed class StoredDocument
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>value (دليل قيمة) أو structure (مستند هيكل كلية).</summary>
    public string OwnerType { get; set; } = "";

    /// <summary>القيمة أو الكلية.</summary>
    public Guid OwnerId { get; set; }

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = "";

    public string StorageKey { get; set; } = "";

    public string? Description { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public uint Version { get; set; }

    public const string ValueOwner = "value";
    public const string StructureOwner = "structure";
}
