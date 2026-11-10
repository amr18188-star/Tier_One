using Tier1.Domain.Collection;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Collection;

/// <summary>حالة الأداء مقابل المستهدف. نقص البيانات أو عدم اعتمادها حالة مستقلة، لا ضعف أداء.</summary>
public enum Performance
{
    Achieved = 1,
    Near = 2,
    Behind = 3,
    NoTarget = 4,
    NoData = 5,

    /// <summary>الاتجاه غير محدد في التعريف، فلا يحكم على القيمة.</summary>
    NotJudged = 6,
}

public sealed record UnitValue(Guid UnitId, ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator);

/// <summary>القيمة المجمعة لجهة أعلى: القيمة، وكم جهة أبلغت من المتوقع، وملاحظة إن لم يصح الجمع.</summary>
public sealed record Aggregate(decimal? Value, decimal? Numerator, decimal? Denominator, int Reported, int Expected, int NotApplicable, string? Note);

/// <summary>
/// تجميع القيم وحكم الأداء (دوال نقية). القاعدة من تعريف المؤشر: المجموع للأعداد، ونسبة مجموع البسط إلى مجموع المقام للنسب،
/// والمتوسط المرجح بالمقام، ولا جمع لعدد مميز أو قياس مركزي (يدخل على مستوى الجامعة مباشرة).
/// </summary>
public static class ResultsRules
{
    /// <summary>حد «قريب من المستهدف»: 90% من المستهدف أو أكثر.</summary>
    public const decimal NearRatio = 0.9m;

    public static Aggregate Combine(Aggregation aggregation, bool percent, IReadOnlyList<UnitValue> values, int expected)
    {
        var numbers = values.Where(v => v.Kind == ValueKind.Number).ToList();
        var notApplicable = values.Count(v => v.Kind == ValueKind.NotApplicable);
        var reported = values.Count;
        if (numbers.Count == 0) return new(null, null, null, reported, expected, notApplicable, null);

        switch (aggregation)
        {
            case Aggregation.Sum:
                return new(numbers.Sum(v => v.Value ?? 0), null, null, reported, expected, notApplicable, null);
            case Aggregation.RatioOfSums:
            {
                var withRatio = numbers.Where(v => v.Numerator is not null && v.Denominator is > 0).ToList();
                var n = withRatio.Sum(v => v.Numerator!.Value);
                var d = withRatio.Sum(v => v.Denominator!.Value);
                return d == 0 ? new(null, n, d, reported, expected, notApplicable, "مجموع المقامات صفر.")
                    : new(decimal.Round(n / d * (percent ? 100 : 1), 4), n, d, reported, expected, notApplicable, null);
            }
            case Aggregation.WeightedAverage:
            {
                var weighted = numbers.Where(v => v.Denominator is > 0).ToList();
                if (weighted.Count == numbers.Count)
                {
                    var w = weighted.Sum(v => v.Denominator!.Value);
                    return new(decimal.Round(weighted.Sum(v => (v.Value ?? 0) * v.Denominator!.Value) / w, 4), null, w, reported, expected, notApplicable, null);
                }
                return new(decimal.Round(numbers.Average(v => v.Value ?? 0), 4), null, null, reported, expected, notApplicable,
                    "متوسط بسيط: لم تُدخل كل الجهات عدد المستجيبين للترجيح.");
            }
            default:
                return numbers.Count == 1 && expected <= 1
                    ? new(numbers[0].Value, numbers[0].Numerator, numbers[0].Denominator, reported, expected, notApplicable, null)
                    : new(null, null, null, reported, expected, notApplicable, aggregation == Aggregation.DistinctCount
                        ? "عدد مميز: لا يجمع من الجهات؛ يقاس على مستوى الجامعة بعد إزالة التكرار."
                        : "لا يجمع من الجهات بحسب قاعدة التجميع.");
        }
    }

    /// <summary>نسبة الإنجاز من المستهدف بحسب الاتجاه: الأعلى أفضل = الفعلي ÷ المستهدف، والأقل أفضل = المستهدف ÷ الفعلي.</summary>
    public static decimal? Achievement(Direction direction, decimal? actual, decimal? target)
    {
        if (actual is not { } a || target is not { } t) return null;
        return direction switch
        {
            Direction.HigherIsBetter => t == 0 ? (a >= 0 ? 1 : 0) : decimal.Round(a / t, 4),
            Direction.LowerIsBetter => a == 0 ? 1 : decimal.Round(t / a, 4),
            _ => null,
        };
    }

    public static Performance Judge(Direction direction, decimal? actual, decimal? target, decimal near = NearRatio)
    {
        if (actual is null) return Performance.NoData;
        if (target is null) return Performance.NoTarget;
        if (direction is not (Direction.HigherIsBetter or Direction.LowerIsBetter)) return Performance.NotJudged;
        var r = Achievement(direction, actual, target)!.Value;
        return r >= 1 ? Performance.Achieved : r >= near ? Performance.Near : Performance.Behind;
    }

    /// <summary>ملخص مقروء بجانب الرسم: «حققت الجامعة 92% من المستهدف، بتحسن 3 نقاط عن الدورة السابقة».</summary>
    public static string Summary(string who, string unit, Direction direction, decimal? actual, decimal? target, decimal? previous)
    {
        if (actual is null) return $"لا توجد قيمة معتمدة لـ{who} بعد.";
        var text = $"{who}: {Fmt(actual.Value, unit)}";
        if (target is not null)
        {
            var ach = Achievement(direction, actual, target);
            text += ach is { } r ? $"، أي {Math.Round(r * 100)}% من المستهدف ({Fmt(target.Value, unit)})" : $" مقابل مستهدف {Fmt(target.Value, unit)}";
        }
        if (previous is { } p)
        {
            var diff = actual.Value - p;
            var better = direction == Direction.LowerIsBetter ? diff < 0 : diff > 0;
            text += diff == 0 ? "، دون تغير عن الدورة السابقة" : $"، {(better ? "بتحسن" : "بتراجع")} {Fmt(Math.Abs(diff), unit == "%" ? "نقطة مئوية" : unit)} عن الدورة السابقة";
        }
        return text + ".";
    }

    private static string Fmt(decimal v, string unit) => unit.Trim() == "%" ? $"{Kpi.DistributionRules.Fmt(v)}%" : $"{Kpi.DistributionRules.Fmt(v)} {unit}";
}
