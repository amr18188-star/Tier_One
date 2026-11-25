using Tier1.Domain.Kpi;

namespace Tier1.Application.Scorecard;

/// <summary>حالة المؤشر مقابل المستهدف النهائي كما في عمود «الحالة» بالبطاقة.</summary>
public enum Level
{
    NoData = 0,
    InLevel = 1,
    Below = 2,
}

/// <summary>مفتاح حالة الأداء (RAG): قوي ≥ 85، قيد التطوير 70–84، معرض للخطر &lt; 70.</summary>
public enum Rag
{
    Strong = 1,
    Developing = 2,
    AtRisk = 3,
}

/// <summary>حدود الفئات: أدنى درجة للركيزة في الفئات 1–4 (الفئة 5 ما دونها)، وزيادة حدود الدرجة الكلية عليها.</summary>
public sealed record TierThresholds(decimal Tier1, decimal Tier2, decimal Tier3, decimal Tier4, decimal OverallOffset)
{
    public static readonly TierThresholds Default = new(85, 70, 55, 40, 5);

    public decimal PillarMin(int tier) => tier switch { 1 => Tier1, 2 => Tier2, 3 => Tier3, 4 => Tier4, _ => 0 };

    /// <summary>أدنى درجة كلية للفئة: حد الركيزة + الزيادة (الفئة 5 تبدأ من الزيادة نفسها كما في الإطار).</summary>
    public decimal OverallMin(int tier) => Math.Min(PillarMin(tier) + OverallOffset, 100);
}

/// <summary>نتيجة الفئة: فئة الدرجة الكلية، وفئة أضعف ركيزة، والفئة النهائية بعد فحص التوازن (Weakest-Link Check).</summary>
public sealed record TierResult(int OverallTier, int WeakestTier, int FinalTier, TierAlert Alert);

public enum TierAlert
{
    /// <summary>لا تفاوت كبير بين الركائز.</summary>
    Balanced = 0,

    /// <summary>أضعف ركيزة أدنى بفئة كاملة: تنبيه دون تخفيض.</summary>
    Warning = 1,

    /// <summary>أضعف ركيزة أدنى بفئتين أو أكثر: تخفض الفئة النهائية فئة واحدة.</summary>
    Downgraded = 2,
}

/// <summary>
/// معادلات «الإطار الشامل لتقييم أداء الجامعات» (KPI.xlsx) كما هي، دوال نقية:
/// <list type="bullet">
/// <item>نسبة الإنجاز = القيمة ÷ المستهدف × 100 (أو المستهدف ÷ القيمة للمؤشر التنازلي)، ولا تتجاوز 100، مقربة لرقم عشري واحد.</item>
/// <item>قيمة السنة: للمؤشر التراكمي آخر ربع أدخل فيها، وللحظي متوسط أرباعها المدخلة.</item>
/// <item>المعيار = متوسط مؤشراته، والركيزة = متوسط معاييرها (وزن متساوٍ للمعايير)، والدرجة الكلية = متوسط الركائز الست.
/// المؤشر بلا قيمة يحسب صفرًا في الدرجة الحالية وفي درجة السنة مقابل المستهدف النهائي، ويستبعد من درجة السنة مقابل مستهدفها.</item>
/// <item>الفئة: حدود الركائز، وحدود الدرجة الكلية أعلى بخمس نقاط، وتخفض الفئة فئة إذا كانت أضعف ركيزة أسوأ بفئتين أو أكثر.</item>
/// </list>
/// الفرق الوحيد عن الملف: المؤشر التصاعدي الذي مستهدفه صفر (نمو التسجيل 2.02) محقق إذا كانت قيمته صفرًا أو أكثر،
/// لا محقق دائمًا كما في الملف حتى مع التراجع.
/// </summary>
public static class ScorecardRules
{
    public static decimal Round1(decimal v) => decimal.Round(v, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// نسبة الإنجاز من المستهدف بعرض ملف الإطار: من المصدر الواحد Kpi.Achievement (D7)، محدودة بـ100 ومقربة لرقم عشري؛ أو null بلا قيمة أو بلا مستهدف.
    /// الاتجاه غير المحدد يعامل تصاعديًا كما في الملف.
    /// </summary>
    public static decimal? Achievement(decimal? value, decimal? target, Direction direction) =>
        Kpi.Achievement.Ratio(Framework(direction), value, target) is { } r ? Round1(Math.Min(r * 100, 100)) : null;

    /// <summary>النسبة الفعلية دون حد الـ100، للعلم فقط حين يتجاوز الأداء المستهدف؛ لا معنى لها مع مستهدف صفر أو فعلي صفر في التنازلي.</summary>
    public static decimal? RawAchievement(decimal? value, decimal? target, Direction direction)
    {
        if (value is not { } v || target is not { } t || t == 0) return null;
        if (direction == Direction.LowerIsBetter && v == 0) return null;
        return Kpi.Achievement.Ratio(Framework(direction), v, t) is { } r ? Round1(r * 100) : null;
    }

    private static Direction Framework(Direction d) => d == Direction.Undefined ? Direction.HigherIsBetter : d;

    /// <summary>
    /// «في المستوى» إذا بلغت أحدث قيمة المستهدف النهائي في اتجاه المؤشر، وإلا «دون المستوى».
    /// G-12 (system integration review 2026-11-24): «الوصول إلى قيمة» يحكم عليه بقاعدة النتائج نفسها (محقق ضمن نصف هامش القريب)،
    /// لا كأنه تصاعدي، فلا تتجاوزه القيمة الأعلى من المستهدف بكثير وتعد «في المستوى».
    /// </summary>
    public static Level LevelOf(decimal? latest, decimal? finalTarget, Direction direction)
    {
        if (latest is not { } v || finalTarget is not { } t) return Level.NoData;
        var ok = direction switch
        {
            Direction.LowerIsBetter => v <= t,
            Direction.Range => Collection.ResultsRules.Judge(Direction.Range, v, t) == Collection.Performance.Achieved,
            _ => v >= t,
        };
        return ok ? Level.InLevel : Level.Below;
    }

    /// <summary>آخر قيمة مدخلة في سلسلة الأرباع.</summary>
    public static (decimal? Value, int Index) Latest(IReadOnlyList<decimal?> quarters)
    {
        for (var i = quarters.Count - 1; i >= 0; i--)
            if (quarters[i] is { } v) return (v, i);
        return (null, -1);
    }

    /// <summary>قيمة السنة من أرباعها الأربعة.</summary>
    public static decimal? YearValue(IReadOnlyList<decimal?> fourQuarters, Accumulation accumulation)
    {
        var known = fourQuarters.Where(q => q is not null).Select(q => q!.Value).ToList();
        if (known.Count == 0) return null;
        return accumulation == Accumulation.Cumulative ? Latest(fourQuarters).Value : known.Average();
    }

    /// <summary>المتوسط مقربًا، أو صفر إن لم توجد قيم (كما يعيد الملف صفرًا عند خطأ المتوسط).</summary>
    public static decimal Average(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : Round1(list.Average());
    }

    /// <summary>متوسط ما وجد من القيم (المستبعد فارغ)، أو صفر إن لم يوجد شيء.</summary>
    public static decimal AverageKnown(IEnumerable<decimal?> values) => Average(values.Where(v => v is not null).Select(v => v!.Value));

    public static Rag RagOf(decimal score) => score >= 85 ? Rag.Strong : score >= 70 ? Rag.Developing : Rag.AtRisk;

    public static int PillarTier(decimal score, TierThresholds t) =>
        score >= t.Tier1 ? 1 : score >= t.Tier2 ? 2 : score >= t.Tier3 ? 3 : score >= t.Tier4 ? 4 : 5;

    public static int OverallTier(decimal score, TierThresholds t) =>
        score >= t.OverallMin(1) ? 1 : score >= t.OverallMin(2) ? 2 : score >= t.OverallMin(3) ? 3 : score >= t.OverallMin(4) ? 4 : 5;

    /// <summary>
    /// الفئة النهائية (منهجية Hybrid): فئة الدرجة الكلية، إلا إذا كانت أضعف ركيزة أسوأ منها بفئتين أو أكثر فتخفض فئة واحدة (لا أدنى من 5).
    /// </summary>
    public static TierResult Tier(decimal overall, decimal weakestPillar, TierThresholds t)
    {
        var o = OverallTier(overall, t);
        var w = PillarTier(weakestPillar, t);
        var gap = w - o;
        var final = Math.Min(gap >= 2 ? o + 1 : o, 5);
        return new(o, w, final, gap >= 2 ? TierAlert.Downgraded : gap >= 1 ? TierAlert.Warning : TierAlert.Balanced);
    }

    /// <summary>النقاط اللازمة للدرجة الكلية لبلوغ الفئة، أو صفر إن بلغتها.</summary>
    public static decimal GapTo(int tier, decimal overall, TierThresholds t) => Math.Max(0, Round1(t.OverallMin(tier) - overall));
}
