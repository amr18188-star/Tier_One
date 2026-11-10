using System.Globalization;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Kpi;

public enum DistributionStatus
{
    /// <summary>توزيع الكليات يحقق مستهدف الجامعة.</summary>
    Covers = 1,

    /// <summary>توزيع الكليات لا يكفي لتحقيق مستهدف الجامعة.</summary>
    Short = 2,

    /// <summary>كليات بلا مستهدف، أو بلا بسط ومقام لنسبة.</summary>
    Incomplete = 3,

    /// <summary>طبيعة المؤشر لا تسمح بالمقارنة بالجمع (عدد مميز، قياس مركزي...).</summary>
    NotComparable = 4,

    NoUniversityTarget = 5,
}

/// <summary>مستهدف كلية كما يدخل الحساب: مستهدفها الخاص، أو الموحد «لكل كلية» إن لم يكن لها خاص.</summary>
public sealed record CollegeShare(Guid UnitId, string Name, decimal? Value, decimal? Numerator, decimal? Denominator, string? Text, bool FromEachCollegeDefault);

public sealed record DistributionResult(
    DistributionStatus Status,
    decimal? UniversityValue,
    decimal? CollegesValue,
    decimal? Gap,
    int CollegeCount,
    int MissingCount,
    string SummaryAr);

/// <summary>
/// مقارنة توزيع مستهدفات الكليات بمستهدف الجامعة (دالة نقية). لا تجمع كل النسب بالطريقة نفسها:
/// المجموع للأعداد، ونسبة مجموع البسط إلى مجموع المقام للنسب، ولا مقارنة لما يقاس مركزيًا أو بعدد مميز.
/// </summary>
public static class DistributionRules
{
    private const decimal Tolerance = 0.0001m;

    public static DistributionResult Compare(Aggregation aggregation, Direction direction, bool isPercent, decimal? universityValue, IReadOnlyList<CollegeShare> colleges)
    {
        if (aggregation is not (Aggregation.Sum or Aggregation.RatioOfSums))
            return new(DistributionStatus.NotComparable, universityValue, null, null, colleges.Count, 0, NotComparableText(aggregation));

        if (universityValue is not { } target)
            return new(DistributionStatus.NoUniversityTarget, null, null, null, colleges.Count, 0, "لا يوجد مستهدف للجامعة لهذه السنة.");

        if (colleges.Count == 0)
            return new(DistributionStatus.Incomplete, target, null, null, 0, 0, "لا توجد كليات أو معاهد مسجلة في الهيكل بعد.");

        return aggregation == Aggregation.Sum
            ? CompareSum(direction, target, colleges)
            : CompareRatio(direction, isPercent, target, colleges);
    }

    private static DistributionResult CompareSum(Direction direction, decimal target, IReadOnlyList<CollegeShare> colleges)
    {
        var missing = colleges.Count(c => c.Value is null);
        var sum = colleges.Sum(c => c.Value ?? 0);
        var gap = sum - target;
        if (missing > 0)
            return new(DistributionStatus.Incomplete, target, sum, gap, colleges.Count, missing,
                $"{missing} من {colleges.Count} كلية بلا مستهدف رقمي. مجموع المتاح {Fmt(sum)} مقابل مستهدف الجامعة {Fmt(target)}.");
        return Judge(direction, target, sum, colleges.Count, "مجموع مستهدفات الكليات");
    }

    private static DistributionResult CompareRatio(Direction direction, bool isPercent, decimal target, IReadOnlyList<CollegeShare> colleges)
    {
        var missing = colleges.Count(c => c.Numerator is null || c.Denominator is null);
        if (missing > 0)
            return new(DistributionStatus.Incomplete, target, null, null, colleges.Count, missing,
                $"النسبة تجمع من البسط والمقام لا من متوسط النسب؛ {missing} من {colleges.Count} كلية بلا بسط ومقام مستهدفين.");
        var den = colleges.Sum(c => c.Denominator!.Value);
        if (den == 0)
            return new(DistributionStatus.Incomplete, target, null, null, colleges.Count, colleges.Count, "مجموع المقامات صفر؛ لا يمكن حساب النسبة.");
        var ratio = colleges.Sum(c => c.Numerator!.Value) / den * (isPercent ? 100 : 1);
        return Judge(direction, target, decimal.Round(ratio, 4), colleges.Count, "النسبة المجمعة من الكليات");
    }

    private static DistributionResult Judge(Direction direction, decimal target, decimal combined, int count, string label)
    {
        var gap = combined - target;
        var covers = direction == Direction.LowerIsBetter ? combined <= target + Tolerance : combined >= target - Tolerance;
        var text = Math.Abs(gap) <= Tolerance
            ? $"{label} يساوي مستهدف الجامعة ({Fmt(target)})."
            : $"{label} {Fmt(combined)} مقابل مستهدف الجامعة {Fmt(target)} — {(gap > 0 ? "أعلى" : "أقل")} بمقدار {Fmt(Math.Abs(gap))}.";
        return new(covers ? DistributionStatus.Covers : DistributionStatus.Short, target, combined, gap, count, 0, text);
    }

    private static string NotComparableText(Aggregation a) => a switch
    {
        Aggregation.DistinctCount => "عدد مميز: لا يجمع من الكليات، يحسب مرة واحدة على مستوى الجامعة بعد إزالة التكرار.",
        Aggregation.Central => "يقاس مركزيًا على مستوى الجامعة؛ لا يوزع على الكليات.",
        Aggregation.WeightedAverage => "متوسط مرجح: يقارن بعد جمع البيانات بأوزانها، لا من المستهدفات.",
        Aggregation.StatusOnly => "حالة لكل جهة؛ لا تجمع.",
        _ => "قاعدة التجميع غير محددة بعد في القاموس.",
    };

    public static string Fmt(decimal v) => v.ToString(v == decimal.Truncate(v) ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);
}
