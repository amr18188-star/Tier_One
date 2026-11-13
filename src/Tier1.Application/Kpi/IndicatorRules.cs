using Tier1.Domain.Common;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Kpi;

/// <summary>
/// قواعد دورة حياة تعريف المؤشر (دوال نقية):
/// مسودة ← قيد المراجعة ← معتمد. تعديل المعتمد ينشئ إصدارًا جديدًا ولا يمس المعتمد حتى يعتمد الجديد.
/// </summary>
public static class IndicatorRules
{
    /// <summary>وحدات لا تكفي لنشر المؤشر.</summary>
    private static readonly HashSet<string> UndefinedUnits = new(StringComparer.Ordinal) { "", "غير محدد" };

    /// <summary>
    /// ما ينقص التعريف ليعتمد: «لا يُنشر مؤشر دون تعريف ووحدة ومصدر ومسؤول وقاعدة احتساب واضحة».
    /// </summary>
    public static IReadOnlyList<string> MissingForApproval(IndicatorVersion v)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(v.Definition)) missing.Add("التعريف");
        if (UndefinedUnits.Contains(v.Unit.Trim()) || v.Unit.Contains('؟', StringComparison.Ordinal)) missing.Add("وحدة القياس");
        if (v.Direction == Direction.Undefined) missing.Add("الاتجاه المطلوب");
        if (v.Aggregation == Aggregation.Undefined) missing.Add("قاعدة التجميع");
        if (v.Aggregation == Aggregation.RatioOfSums && (string.IsNullOrWhiteSpace(v.Numerator) || string.IsNullOrWhiteSpace(v.Denominator)))
            missing.Add("البسط والمقام");
        if (v.Aggregation == Aggregation.WeightedAverage && string.IsNullOrWhiteSpace(v.Denominator))
            missing.Add("أساس الترجيح");
        if (AggregationProblem(v) is { } problem) missing.Add(problem);
        if (string.IsNullOrWhiteSpace(v.DataSource)) missing.Add("مصدر البيانات");
        if (v.OwnerUnitId is null) missing.Add("الجهة المالكة");
        return missing;
    }

    /// <summary>
    /// قاعدة تجميع تعطي رقمًا غير دقيق للجامعة: النسب والمعدلات لا تجمع (تحسب من مجموع البسط إلى مجموع المقام)،
    /// والقياس المركزي يدخل على مستوى الجامعة لا من الكليات.
    /// </summary>
    public static string? AggregationProblem(IndicatorVersion v)
    {
        var unit = v.Unit.Trim();
        var rate = unit == "%" || unit.Contains("لكل", StringComparison.Ordinal) || unit.Contains('/', StringComparison.Ordinal);
        if (v.Aggregation == Aggregation.Sum && rate && v.Level != CollectionLevel.University)
            return "قاعدة التجميع: النسب والمعدلات لا تجمع؛ اجعلها «نسبة مجموع البسط إلى مجموع المقام» أو «متوسط مرجح»";
        if (v.Aggregation == Aggregation.Central && v.Level != CollectionLevel.University)
            return "قاعدة التجميع: «يقاس مركزيًا» لمؤشر على مستوى الجامعة فقط";
        return null;
    }

    /// <summary>
    /// الإصدار الذي يقبل التعديل الآن: قيد العمل إن وجد (ويعود قيد المراجعة مسودة)، وإلا نسخة جديدة من المعتمد.
    /// </summary>
    public static IndicatorVersion EditableVersion(Indicator indicator, DateTimeOffset now, string? changeReason)
    {
        if (indicator.Suspended)
            throw new DomainException("indicator_suspended", "المؤشر موقوف؛ أعد تفعيله قبل تعديل تعريفه.", "The indicator is suspended; reactivate it first.");

        if (indicator.Working is { } working)
        {
            working.Status = DefinitionStatus.Draft;
            working.SubmittedAt = null;
            working.SubmittedBy = null;
            return working;
        }

        var approved = indicator.Effective
            ?? throw new InvalidOperationException($"Indicator {indicator.Code} has neither a working nor an approved version.");
        if (string.IsNullOrWhiteSpace(changeReason) || changeReason.Trim().Length < 5)
            throw new DomainException("change_reason_required",
                "التعريف معتمد؛ تعديله ينشئ إصدارًا جديدًا. اكتب سبب التعديل.",
                "The definition is approved; editing creates a new version. Enter the reason.", 400);

        var next = Copy(approved);
        next.Number = indicator.Versions.Max(v => v.Number) + 1;
        next.ChangeReason = changeReason.Trim();
        next.CreatedAt = now;
        indicator.Versions.Add(next);
        return next;
    }

    public static void Submit(IndicatorVersion v, Guid by, DateTimeOffset now)
    {
        if (v.Status != DefinitionStatus.Draft)
            throw new DomainException("not_draft", "يرسل للمراجعة التعريف المسودة فقط.", "Only a draft can be submitted.");
        var missing = MissingForApproval(v);
        if (missing.Count > 0)
            throw new DomainException("definition_incomplete",
                "أكمل قبل الإرسال: " + string.Join("، ", missing) + ".",
                "Complete before submitting: " + string.Join(", ", missing) + ".");
        v.Status = DefinitionStatus.UnderReview;
        v.SubmittedBy = by;
        v.SubmittedAt = now;
        v.ReturnNote = null;
    }

    /// <summary>
    /// الاعتماد بقرار اللجنة العليا. من أعدّ التعريف أو عدّله لا يعتمده (فصل المهام)، والإصدار السابق يصبح «حل محله».
    /// </summary>
    public static void Approve(Indicator indicator, IndicatorVersion v, Guid by, DateTimeOffset now, string decisionRef)
    {
        if (v.Status != DefinitionStatus.UnderReview)
            throw new DomainException("not_under_review", "يعتمد التعريف المرسل للمراجعة فقط.", "Only a definition under review can be approved.");
        if (v.EditedBy == by || v.SubmittedBy == by)
            throw DomainException.SeparationOfDuties("SOD-K1",
                "من أعدّ التعريف أو أرسله لا يعتمده.",
                "Whoever prepared or submitted the definition cannot approve it.");
        if (string.IsNullOrWhiteSpace(decisionRef))
            throw new DomainException("decision_ref_required", "اكتب رقم محضر اللجنة العليا الذي اعتمد التعريف.", "Enter the committee minutes reference.", 400);

        foreach (var old in indicator.Versions.Where(x => x.Status == DefinitionStatus.Approved))
            old.Status = DefinitionStatus.Superseded;
        v.Status = DefinitionStatus.Approved;
        v.ApprovedBy = by;
        v.ApprovedAt = now;
        v.DecisionRef = decisionRef.Trim();
    }

    public static void Return(IndicatorVersion v, string note)
    {
        if (v.Status != DefinitionStatus.UnderReview)
            throw new DomainException("not_under_review", "يعاد التعريف المرسل للمراجعة فقط.", "Only a definition under review can be returned.");
        v.Status = DefinitionStatus.Draft;
        v.ReturnNote = note;
        v.SubmittedAt = null;
        v.SubmittedBy = null;
    }

    private static IndicatorVersion Copy(IndicatorVersion s) => new()
    {
        IndicatorId = s.IndicatorId,
        NameAr = s.NameAr,
        NameEn = s.NameEn,
        AlternateName = s.AlternateName,
        Definition = s.Definition,
        Purpose = s.Purpose,
        Exclusions = s.Exclusions,
        Numerator = s.Numerator,
        Denominator = s.Denominator,
        Formula = s.Formula,
        Accumulation = s.Accumulation,
        Unit = s.Unit,
        Direction = s.Direction,
        Frequency = s.Frequency,
        Level = s.Level,
        Aggregation = s.Aggregation,
        AggregationNote = s.AggregationNote,
        DataSource = s.DataSource,
        OwnerUnitId = s.OwnerUnitId,
        EvidenceRequired = s.EvidenceRequired,
        SourcePages = s.SourcePages,
    };
}
