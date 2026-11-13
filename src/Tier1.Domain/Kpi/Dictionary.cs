namespace Tier1.Domain.Kpi;

/// <summary>ركيزة من ركائز الخطة (من ملف مؤشرات الكليات).</summary>
public sealed class Pillar
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    /// <summary>شرح الركيزة وما تهدف إليه، يظهر في شجرة الخطة.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public uint Version { get; set; }
}

/// <summary>معيار من المعايير الاثني عشر لنموذج Tier 1.</summary>
public sealed class Standard
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    /// <summary>شرح المعيار وما يقيسه.</summary>
    public string? Description { get; set; }

    public Guid? PillarId { get; set; }

    public int SortOrder { get; set; }

    public string? SourcePages { get; set; }

    /// <summary>ملاحظة تعارض التصنيف بين الملفين، إن وجدت.</summary>
    public string? ClassificationNote { get; set; }

    public uint Version { get; set; }
}

public enum Direction
{
    Undefined = 0,
    HigherIsBetter = 1,
    LowerIsBetter = 2,

    /// <summary>الوصول إلى قيمة بعينها: الأقرب إلى المستهدف أفضل (ساعات التدريس الأسبوعية لعضو هيئة التدريس مثلًا).</summary>
    Range = 3,
}

public enum Aggregation
{
    Undefined = 0,

    /// <summary>مجموع قيم الجهات.</summary>
    Sum = 1,

    /// <summary>نسبة مجموع البسط إلى مجموع المقام، لا متوسط النسب.</summary>
    RatioOfSums = 2,

    /// <summary>عدد مميز بعد إزالة التكرار (الدول الممثلة، الأبحاث المشتركة).</summary>
    DistinctCount = 3,

    /// <summary>متوسط مرجح (الاستبيانات بعدد المستجيبين).</summary>
    WeightedAverage = 4,

    /// <summary>يقاس مركزيًا على مستوى الجامعة.</summary>
    Central = 5,

    /// <summary>حالة لكل جهة لا تجمع.</summary>
    StatusOnly = 6,
}

/// <summary>
/// نوع المؤشر بحسب طريقة الحساب في الإطار الشامل: قيمة السنة للمؤشر التراكمي آخر ربع أدخل فيها (إجمالي حتى تاريخه)،
/// وللمؤشر اللحظي متوسط أرباعها.
/// </summary>
public enum Accumulation
{
    /// <summary>غير تراكمي (لحظي): القيمة عند تاريخ القياس.</summary>
    Snapshot = 1,

    /// <summary>تراكمي: إجمالي القيمة المتراكمة حتى الآن.</summary>
    Cumulative = 2,
}

public enum CollectionLevel
{
    Department = 1,
    College = 2,
    University = 3,
}

public enum Frequency
{
    Monthly = 1,
    Quarterly = 2,
    SemiAnnual = 3,
    Annual = 4,
    Custom = 5,
}

public enum DefinitionStatus
{
    Draft = 1,
    UnderReview = 2,
    Approved = 3,

    /// <summary>حل محله إصدار معتمد أحدث؛ يبقى للنتائج التي قيست به.</summary>
    Superseded = 4,
}

/// <summary>
/// مؤشر في القاموس. الكود ثابت لا يعاد استخدامه. التعريف نفسه في إصدارات: تعديل تعريف معتمد ينشئ إصدارًا جديدًا
/// يمر بالمراجعة والاعتماد، ويبقى المعتمد ساريًا حتى يعتمد الجديد، فلا تعاد كتابة نتائج قيست بالتعريف القديم.
/// </summary>
public sealed class Indicator
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Code { get; set; } = "";

    public Guid StandardId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>موقوف: لا يدخل دورات القياس الجديدة، ويبقى بتاريخه.</summary>
    public bool Suspended { get; set; }

    /// <summary>أكواد التعارضات المرتبطة من سجل التعارضات، للعرض والانتقال.</summary>
    public List<string> ConflictCodes { get; set; } = [];

    /// <summary>
    /// أول سنة يطلب فيها مستهدف للمؤشر (مؤشر يبدأ بعد سنتين مثلًا)؛ قبلها «لم يبدأ» ولا يحكم على الأداء. فارغ = من أول سنة في الخطة.
    /// </summary>
    public int? PlanStartYear { get; set; }

    /// <summary>سنة تحقيق المستهدف النهائي (قصير المدى بعد سنتين أو ثلاث، أو نهاية الخطة). بعدها يستمر آخر مستهدف.</summary>
    public int? PlanEndYear { get; set; }

    /// <summary>شرح المستهدف ومراحله: لماذا هذه القيم وكيف يتدرج الوصول إليها.</summary>
    public string? PlanNote { get; set; }

    /// <summary>
    /// مناظير بطاقة أداء القطاع العام (PSS) التي يخدمها المؤشر، من 1 إلى 7: الاستراتيجي، ومستخدمو الخدمة وأصحاب المصلحة، والمالي،
    /// وتقديم الخدمة والتميز التشغيلي، والابتكار والتعلم، والقيادة، والأفراد والشراكات والموارد.
    /// </summary>
    public List<int> Perspectives { get; set; } = [];

    /// <summary>هدف التنمية المستدامة الأساسي (1–17).</summary>
    public int? SdgPrimary { get; set; }

    /// <summary>الهدف الثانوي.</summary>
    public int? SdgSecondary { get; set; }

    /// <summary>هدف الأثر النهائي (المستحث).</summary>
    public int? SdgInduced { get; set; }

    /// <summary>لماذا ربط المؤشر بهذه المناظير.</summary>
    public string? PssRationale { get; set; }

    /// <summary>لماذا ربط المؤشر بهذه الأهداف.</summary>
    public string? SdgRationale { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }

    public List<IndicatorVersion> Versions { get; set; } = [];

    public IndicatorVersion? Effective => Versions.Where(v => v.Status == DefinitionStatus.Approved).MaxBy(v => v.Number);

    /// <summary>الإصدار قيد الإعداد أو المراجعة، إن وجد؛ واحد على الأكثر.</summary>
    public IndicatorVersion? Working => Versions.Where(v => v.Status is DefinitionStatus.Draft or DefinitionStatus.UnderReview).MaxBy(v => v.Number);

    /// <summary>ما يعرض افتراضيًا: الإصدار قيد العمل إن وجد، وإلا المعتمد.</summary>
    public IndicatorVersion Current => Working ?? Effective ?? Versions.MaxBy(v => v.Number)!;
}

/// <summary>إصدار من تعريف المؤشر.</summary>
public sealed class IndicatorVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid IndicatorId { get; set; }

    public int Number { get; set; } = 1;

    public DefinitionStatus Status { get; set; } = DefinitionStatus.Draft;

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    /// <summary>الاسم في ملف Tier 1 إن اختلف عن ملف الكليات.</summary>
    public string? AlternateName { get; set; }

    public string? Definition { get; set; }

    public string? Purpose { get; set; }

    public string? Exclusions { get; set; }

    public string? Numerator { get; set; }

    public string? Denominator { get; set; }

    /// <summary>معادلة التقييم كما كتبت في الإطار (متى تقاس وكيف تحسب).</summary>
    public string? Formula { get; set; }

    public Accumulation Accumulation { get; set; } = Accumulation.Snapshot;

    public string Unit { get; set; } = "%";

    public Direction Direction { get; set; }

    public Frequency Frequency { get; set; } = Frequency.Annual;

    public CollectionLevel Level { get; set; } = CollectionLevel.College;

    public Aggregation Aggregation { get; set; }

    public string? AggregationNote { get; set; }

    public string? DataSource { get; set; }

    /// <summary>الجهة المالكة للمؤشر.</summary>
    public Guid? OwnerUnitId { get; set; }

    public string? EvidenceRequired { get; set; }

    public string? SourcePages { get; set; }

    /// <summary>سبب الإصدار الجديد (من الإصدار الثاني فصاعدًا).</summary>
    public string? ChangeReason { get; set; }

    public Guid? EditedBy { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public Guid? SubmittedBy { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>رقم محضر اللجنة العليا لـ Tier 1 الذي اعتمد التعريف.</summary>
    public string? DecisionRef { get; set; }

    public string? ReturnNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }
}
