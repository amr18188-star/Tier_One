namespace Tier1.Application.Scorecard;

/// <summary>ربع من أرباع الخطة: سنة الخطة (باسم السنة التي تنتهي فيها)، ورقمه في السنة، وترتيبه في الخطة (1…)، وتاريخا بدايته ونهايته.</summary>
public sealed record PlanQuarter(int Year, int YearIndex, int Quarter, int Index, DateOnly Start, DateOnly End, string Label, bool Current);

/// <summary>
/// فترة الخطة (ADR-0017): من أول شهر البداية إلى آخر يوم في شهر النهاية، يحددها المدير الأعلى بتعليمات وزارة التعليم العالي
/// (الافتراضي 1 سبتمبر 2026 إلى 31 أغسطس 2031). الفترة سنوات خطة كاملة من اثني عشر شهرًا، في كل سنة أربعة أرباع من ثلاثة أشهر.
/// تسمى سنة الخطة بالسنة الميلادية التي تنتهي فيها (سبتمبر 2026 – أغسطس 2027 هي 2027)، وهي سنة مستهدفاتها،
/// وخط الأساس للسنة التي قبل الأولى.
/// </summary>
public sealed record PlanPeriod(int StartYear, int StartMonth, int EndYear, int EndMonth)
{
    public const int MaxYears = 10;

    public static readonly PlanPeriod Default = new(2026, 9, 2031, 8);

    public DateOnly Start => new(StartYear, StartMonth, 1);

    public DateOnly End => new DateOnly(EndYear, EndMonth, 1).AddMonths(1).AddDays(-1);

    public int Months => EndYear * 12 + EndMonth - (StartYear * 12 + StartMonth) + 1;

    public int YearCount => Months / 12;

    public int QuarterCount => YearCount * 4;

    /// <summary>The plan years by name: the calendar year in which each twelve-month plan year ends.</summary>
    public IReadOnlyList<int> Years => Enumerable.Range(1, YearCount).Select(k => Start.AddMonths(12 * k).AddDays(-1).Year).ToList();

    public int FirstYear => Years[0];

    public int LastYear => Years[^1];

    /// <summary>The year of the baselines: the one before the first plan year.</summary>
    public int BaselineYear => FirstYear - 1;

    /// <summary>The plan year's number (1…), or 0 outside the plan.</summary>
    public int YearIndex(int year) => year - FirstYear + 1 is var k && k >= 1 && k <= YearCount ? k : 0;

    public bool HasQuarter(int year, int quarter) => YearIndex(year) > 0 && quarter is >= 1 and <= 4;

    /// <summary>The quarter's place in the plan (0…QuarterCount-1), or -1 outside it.</summary>
    public int Slot(int year, int quarter) => HasQuarter(year, quarter) ? (YearIndex(year) - 1) * 4 + quarter - 1 : -1;

    /// <summary>The plan year in progress on a date, by place (0…YearCount-1): the first before the plan starts, the last after it ends.</summary>
    public int YearPlaceOn(DateOnly today) =>
        Math.Clamp((int)Math.Floor((today.Year * 12 + today.Month - (StartYear * 12 + StartMonth)) / 12m), 0, YearCount - 1);

    public string Label(int year, int quarter) => $"السنة {YearIndex(year)} ({year}) — الربع {quarter}";

    public IReadOnlyList<PlanQuarter> Quarters(DateOnly today)
    {
        var list = new List<PlanQuarter>();
        foreach (var year in Years)
            for (var q = 1; q <= 4; q++)
            {
                var slot = Slot(year, q);
                var start = Start.AddMonths(slot * 3);
                var end = start.AddMonths(3).AddDays(-1);
                list.Add(new PlanQuarter(year, YearIndex(year), q, slot + 1, start, end, Label(year, q), today >= start && today <= end));
            }
        return list;
    }

    /// <summary>Why a period cannot be the plan's, or null: whole plan years of twelve months, from one to ten.</summary>
    public static string? Problem(PlanPeriod p)
    {
        if (p.StartMonth is < 1 or > 12 || p.EndMonth is < 1 or > 12) return "الشهر من 1 إلى 12.";
        if (p.Months < 12) return "نهاية الخطة بعد بدايتها بسنة على الأقل.";
        if (p.Months % 12 != 0) return "الخطة سنوات كاملة من اثني عشر شهرًا (أربعة أرباع في كل سنة)؛ اجعل النهاية في الشهر السابق لشهر البداية.";
        if (p.YearCount > MaxYears) return $"الخطة {MaxYears} سنوات على الأكثر.";
        return null;
    }
}

public sealed record NamedItem(int Number, string NameAr, string NameEn);

/// <summary>أسماء مناظير بطاقة أداء القطاع العام (Max Moullin) وأهداف التنمية المستدامة والفئات كما في الإطار.</summary>
public static class FrameworkNames
{
    public static readonly IReadOnlyList<NamedItem> Perspectives =
    [
        new(1, "المنظور الاستراتيجي", "Strategic"),
        new(2, "منظور مستخدمي الخدمة وأصحاب المصلحة", "Service users & stakeholders"),
        new(3, "المنظور المالي", "Financial"),
        new(4, "منظور تقديم الخدمة والتميز التشغيلي", "Service delivery & operational excellence"),
        new(5, "منظور الابتكار والتعلم", "Innovation & learning"),
        new(6, "منظور القيادة", "Leadership"),
        new(7, "منظور الأفراد والشراكات والموارد", "People, partnerships & resources"),
    ];

    public static readonly IReadOnlyList<NamedItem> Sdgs =
    [
        new(1, "القضاء على الفقر", "No Poverty"),
        new(2, "القضاء على الجوع", "Zero Hunger"),
        new(3, "الصحة الجيدة والرفاهية", "Good Health and Well-being"),
        new(4, "التعليم الجيد", "Quality Education"),
        new(5, "المساواة بين الجنسين", "Gender Equality"),
        new(6, "المياه النظيفة والنظافة الصحية", "Clean Water and Sanitation"),
        new(7, "طاقة نظيفة وبأسعار معقولة", "Affordable and Clean Energy"),
        new(8, "العمل اللائق ونمو الاقتصاد", "Decent Work and Economic Growth"),
        new(9, "الصناعة والابتكار والهياكل الأساسية", "Industry, Innovation and Infrastructure"),
        new(10, "الحد من أوجه عدم المساواة", "Reduced Inequalities"),
        new(11, "مدن ومجتمعات محلية مستدامة", "Sustainable Cities and Communities"),
        new(12, "الاستهلاك والإنتاج المسؤولان", "Responsible Consumption and Production"),
        new(13, "العمل المناخي", "Climate Action"),
        new(14, "الحياة تحت الماء", "Life Below Water"),
        new(15, "الحياة في البر", "Life on Land"),
        new(16, "السلام والعدل والمؤسسات القوية", "Peace, Justice and Strong Institutions"),
        new(17, "عقد الشراكات لتحقيق الأهداف", "Partnerships for the Goals"),
    ];

    public static readonly IReadOnlyList<NamedItem> Tiers =
    [
        new(1, "جامعة بحثية عالمية", "Global Research University"),
        new(2, "جامعة وطنية للبحث والابتكار", "National Research & Innovation University"),
        new(3, "جامعة شاملة", "Comprehensive University"),
        new(4, "جامعة إقليمية تعليمية", "Regional Teaching University"),
        new(5, "مؤسسة ناشئة / قيد التطوير", "Emerging / Developing Institution"),
    ];
}
