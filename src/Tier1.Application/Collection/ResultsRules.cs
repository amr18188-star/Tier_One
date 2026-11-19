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

    /// <summary>سنة القياس قبل سنة بدء مستهدف المؤشر في الخطة.</summary>
    NotStarted = 7,

    /// <summary>قيمة مجمعة من بعض الجهات فقط (مجموع أو نسبة أو متوسط): لا يحكم عليها قبل اكتمال الجهات، حتى لا تبدو متأخرة وهي ناقصة.</summary>
    Incomplete = 8,
}

public sealed record UnitValue(Guid UnitId, ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator);

/// <summary>
/// القيمة المجمعة لجهة أعلى، وملاحظة إن لم يصح الجمع أو لم يكتمل (D6، ADR-0005):
/// Reported: الجهات التي أبلغت رقمًا. Expected: الجهات المطلوب منها رقم، أي المتوقعة عدا «غير منطبق».
/// «غير متاح» بيانات ناقصة: لا رقم ولا صفر، فلا تحسب ضمن Reported ويبقى المجموع جزئيًا.
/// </summary>
public sealed record Aggregate(decimal? Value, decimal? Numerator, decimal? Denominator, int Reported, int Expected, int NotApplicable, string? Note, int NotAvailable = 0)
{
    /// <summary>أبلغت كل الجهات المطلوب منها رقم.</summary>
    public bool Complete => Reported >= Expected;
}

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
        var notAvailable = values.Count(v => v.Kind == ValueKind.NotAvailable);
        // «غير منطبق» يخرج الجهة من المطلوب، و«غير متاح» يبقيها مطلوبة بلا رقم (D6).
        var due = Math.Max(0, expected - notApplicable);
        var reported = numbers.Count;
        Aggregate Of(decimal? value, decimal? n, decimal? d, string? note) => new(value, n, d, reported, due, notApplicable, note, notAvailable);
        string? Partial(string what) => reported < due
            ? $"{what} جزئي: اعتمدت أرقام {reported} من {due} جهة{(notAvailable > 0 ? $"، و«غير متاح» من {notAvailable}" : "")}؛ لا يحكم عليه قبل اكتمالها."
            : null;
        if (numbers.Count == 0) return Of(null, null, null, notAvailable > 0 ? $"«غير متاح» من {notAvailable} جهة." : null);

        switch (aggregation)
        {
            case Aggregation.Sum:
                return Of(numbers.Sum(v => v.Value ?? 0), null, null, Partial("مجموع"));
            case Aggregation.RatioOfSums:
            {
                var withRatio = numbers.Where(v => v.Numerator is not null && v.Denominator is > 0).ToList();
                var n = withRatio.Sum(v => v.Numerator!.Value);
                var d = withRatio.Sum(v => v.Denominator!.Value);
                return d == 0 ? Of(null, n, d, "مجموع المقامات صفر.") : Of(decimal.Round(n / d * (percent ? 100 : 1), 4), n, d, Partial("نسبة"));
            }
            case Aggregation.WeightedAverage:
            {
                var weighted = numbers.Where(v => v.Denominator is > 0).ToList();
                if (weighted.Count == numbers.Count)
                {
                    var w = weighted.Sum(v => v.Denominator!.Value);
                    return Of(decimal.Round(weighted.Sum(v => (v.Value ?? 0) * v.Denominator!.Value) / w, 4), null, w, Partial("متوسط"));
                }
                return Of(decimal.Round(numbers.Average(v => v.Value ?? 0), 4), null, null,
                    Partial("متوسط") ?? "متوسط بسيط: لم تُدخل كل الجهات عدد المستجيبين للترجيح.");
            }
            default:
                return numbers.Count == 1 && expected <= 1
                    ? Of(numbers[0].Value, numbers[0].Numerator, numbers[0].Denominator, null)
                    : Of(null, null, null, aggregation == Aggregation.DistinctCount
                        ? "عدد مميز: لا يجمع من الجهات؛ يقاس على مستوى الجامعة بعد إزالة التكرار."
                        : "لا يجمع من الجهات بحسب قاعدة التجميع.");
        }
    }

    /// <summary>
    /// نسبة الإنجاز من المستهدف بحسب الاتجاه، من المصدر الواحد Kpi.Achievement (D7)، مقربة لأربع خانات ودون حد أعلى:
    /// الأعلى أفضل = الفعلي ÷ المستهدف، والأقل أفضل = المستهدف ÷ الفعلي، والوصول إلى قيمة = 1 − نسبة الانحراف عنها (12 مستهدفة و13 فعلية = 92%).
    /// </summary>
    public static decimal? Achievement(Direction direction, decimal? actual, decimal? target) =>
        Kpi.Achievement.Ratio(direction, actual, target) is { } r ? decimal.Round(r, 4) : null;

    /// <summary>
    /// حصة الكلية من مستهدف الجامعة في مؤشر مجموع (عدد): كم يساهم مستهدفها أو ما حققته في رقم الجامعة المرجو.
    /// لا معنى لها في النسب والمتوسطات (تقارن هناك بمستهدف الجامعة نفسه)، ولا مع مستهدف جامعة صفري.
    /// </summary>
    public static decimal? ShareOfUniversity(decimal? part, decimal? universityTarget) =>
        part is { } p && universityTarget is { } u && u != 0 ? decimal.Round(p / u, 4) : null;

    /// <summary>
    /// الحكم على القيمة. للوصول إلى قيمة: محقق إن كان الانحراف نصف هامش «القريب» أو أقل (5% افتراضيًا)، وقريب حتى الهامش كله (10%).
    /// </summary>
    public static Performance Judge(Direction direction, decimal? actual, decimal? target, decimal near = NearRatio)
    {
        if (actual is null) return Performance.NoData;
        if (target is null) return Performance.NoTarget;
        if (direction is not (Direction.HigherIsBetter or Direction.LowerIsBetter or Direction.Range)) return Performance.NotJudged;
        var r = Achievement(direction, actual, target)!.Value;
        var achieved = direction == Direction.Range ? 1 - (1 - near) / 2 : 1;
        return r >= achieved ? Performance.Achieved : r >= near ? Performance.Near : Performance.Behind;
    }

    /// <summary>
    /// حالة المؤشر للجهة: «لم يبدأ» قبل سنة بدئه، و«بيانات جزئية» لأي قيمة مجمعة لم تعتمد كل جهاتها أرقامها بعد
    /// (مجموع أو نسبة أو متوسط؛ و«غير متاح» نقص لا رقم)، وإلا الحكم مقابل المستهدف. نقص البيانات حالة مستقلة لا ضعف أداء (4-10-1، D6).
    /// </summary>
    public static Performance Status(Aggregation aggregation, Direction direction, Aggregate actual, decimal? target, bool notStarted, decimal near = NearRatio)
    {
        if (notStarted) return Performance.NotStarted;
        if (actual.Value is null) return Performance.NoData;
        if (!actual.Complete) return Performance.Incomplete;
        return Judge(direction, actual.Value, target, near);
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
            var better = direction switch
            {
                Direction.LowerIsBetter => diff < 0,
                Direction.Range when target is { } t => Math.Abs(actual.Value - t) < Math.Abs(p - t),
                _ => diff > 0,
            };
            text += diff == 0 ? "، دون تغير عن الدورة السابقة" : $"، {(better ? "بتحسن" : "بتراجع")} {Fmt(Math.Abs(diff), unit == "%" ? "نقطة مئوية" : unit)} عن الدورة السابقة";
        }
        return text + ".";
    }

    private static string Fmt(decimal v, string unit) => unit.Trim() == "%" ? $"{Kpi.DistributionRules.Fmt(v)}%" : $"{Kpi.DistributionRules.Fmt(v)} {unit}";
}
