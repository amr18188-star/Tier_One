namespace Tier1.Application.Scorecard;

/// <summary>ربع من أرباع الخطة العشرين: السنة (2027–2031)، ورقمه في السنة، وترتيبه (1–20)، وتاريخا بدايته ونهايته.</summary>
public sealed record PlanQuarter(int Year, int YearIndex, int Quarter, int Index, DateOnly Start, DateOnly End, string Label, bool Current);

/// <summary>
/// تقويم الخطة: خمس سنوات (2027 هي السنة الأولى) في كل منها أربعة أرباع من ثلاثة أشهر. بداية الربع الأول من السنة الأولى
/// يحددها المدير من الإعدادات (سبتمبر 2026 افتراضيًا، كما في بوابة المجلس الأعلى للجامعات).
/// </summary>
public static class PlanCalendar
{
    public const int FirstYear = 2027;
    public const int LastYear = 2031;
    public static readonly int[] Years = [2027, 2028, 2029, 2030, 2031];

    public static int YearIndex(int year) => year - FirstYear + 1;

    /// <summary>ترتيب الربع في الأرباع العشرين (0–19).</summary>
    public static int Slot(int year, int quarter) => (year - FirstYear) * 4 + quarter - 1;

    public static string Label(int year, int quarter) => $"السنة {YearIndex(year)} ({year}) — الربع {quarter}";

    public static IReadOnlyList<PlanQuarter> Quarters(int startYear, int startMonth, DateOnly today)
    {
        var first = new DateOnly(startYear, startMonth, 1);
        var list = new List<PlanQuarter>();
        foreach (var year in Years)
            for (var q = 1; q <= 4; q++)
            {
                var slot = Slot(year, q);
                var start = first.AddMonths(slot * 3);
                var end = start.AddMonths(3).AddDays(-1);
                list.Add(new PlanQuarter(year, YearIndex(year), q, slot + 1, start, end, Label(year, q), today >= start && today <= end));
            }
        return list;
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
