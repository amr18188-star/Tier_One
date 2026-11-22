using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Kpi;

/// <summary>من أين جاء المستهدف الذي قورنت به الجهة.</summary>
public enum TargetSource
{
    None = 0,

    /// <summary>مستهدف الجهة نفسها.</summary>
    Unit = 1,

    /// <summary>المستهدف الموحد لكل كلية.</summary>
    EachCollege = 2,

    /// <summary>
    /// مستهدف الجامعة: للجهة المركزية التي تدخل رقم الجامعة كلها، وللقسم في نسبة أو متوسط بلا مستهدف خاص.
    /// لا تقاس به الكلية (ADR-0017): الكلية تقاس بمستهدفاتها، ويعرض مستهدف الجامعة بجانبها فقط.
    /// </summary>
    University = 3,
}

/// <summary>
/// المستهدف الساري لجهة في سنة: القيمة ومصدرها وسنته، أو «لم يبدأ» قبل سنة بدء المؤشر في الخطة.
/// PendingValue وPendingYear: مستهدف مقترح كان سيسري لو اعتمد (D3)؛ للعرض «بانتظار الاعتماد» فقط، ولا يدخل أي حساب.
/// </summary>
public sealed record TargetInfo(decimal? Value, TargetSource Source, int? Year, bool NotStarted, decimal? PendingValue = null, int? PendingYear = null,
    ShownTarget? Shown = null)
{
    public bool HasPending => PendingYear is not null;

    /// <summary>What the target column shows beside the value; see <see cref="ShownTarget"/>.</summary>
    public ShownTarget Display => Shown ?? ShownTarget.None;

    public static readonly TargetInfo None = new(null, TargetSource.None, null, false);
}

/// <summary>
/// ما يعرض في عمود «مستهدف الكلية» بجانب قيمة المؤشر (ADR-0017، DEC-5): مستهدف الجهة إن وجد (الخاص، وإلا الموحد لكل كلية)،
/// رقمًا أو نصًا كما سجل؛ وإن لم يوجد فمستهدف الجامعة للعام نفسه، ومصدره <see cref="TargetSource.University"/> فيعرض وتحته خط.
/// **للعرض فقط**: إنجاز الكلية يحسب من مستهدفها وحده (<see cref="TargetInfo.Value"/>)، ولا يحسب من مستهدف الجامعة المعروض مكانه.
/// </summary>
public sealed record ShownTarget(decimal? Value, string? Text, TargetSource Source)
{
    public static readonly ShownTarget None = new(null, null, TargetSource.None);
}

/// <summary>
/// المستهدف الساري (دالة نقية)، بالقواعد نفسها في النماذج والنتائج وبطاقة الأداء والتقدم:
/// **المعتمد وحده** يقاس عليه الأداء (D3)؛ فالمقترح لا يغير مقياس جهة قبل أن تعتمده اللجنة العليا، ويسري آخر معتمد قبله.
/// قبل سنة بدء المؤشر «لم يبدأ»؛ وفي السنة نفسها مستهدفها، وإلا آخر مستهدف محدد قبلها (يستمر المستهدف بعد سنة تحقيقه)؛
/// والكلية بلا مستهدف خاص يسري عليها الموحد لكل كلية، وإلا فلا مستهدف لها (ADR-0017): لا تقاس الكلية بمستهدف الجامعة،
/// ويعرض بجانب مستهدفها. القسم في نسبة أو متوسط بلا مستهدف خاص يقاس بمستهدف الجامعة كما كان.
/// المجموع لا يقارن بمستهدف الجامعة، لأن الجامعة مجموع الكليات.
/// </summary>
public static class TargetLookup
{
    /// <summary>مؤشرات قيمتها لا تتغير بحجم الجهة (نسبة، معدل، متوسط)، فتقارن الكلية بمستهدف الجامعة نفسه.</summary>
    public static bool ComparableAcrossLevels(Aggregation aggregation) => aggregation is Aggregation.RatioOfSums or Aggregation.WeightedAverage;

    public static TargetInfo For(IReadOnlyCollection<IndicatorTarget> targets, OrgUnit unit, Guid universityId, int year, int? planStart, Aggregation aggregation)
    {
        if (planStart is { } start && year < start) return new(null, TargetSource.None, start, true);
        var approved = targets.Where(IsApproved).ToList();
        var (info, row) = Resolve(approved, unit, universityId, year, aggregation);
        info = info with { Shown = Show(approved, unit, universityId, year) };
        // المقترح الذي كان سيسري لو احتسب كل شيء: يعرض «بانتظار الاعتماد» بجانب المعتمد، ولا يمسه.
        var (_, anyRow) = Resolve(targets, unit, universityId, year, aggregation);
        return anyRow is { Status: TargetStatus.Proposed } pending && !ReferenceEquals(pending, row)
            ? info with { PendingValue = pending.Value, PendingYear = pending.Year }
            : info;
    }

    private static bool IsApproved(IndicatorTarget t) => t.Status == TargetStatus.Approved;

    /// <summary>قواعد الأسبقية على مجموعة مستهدفات: مستهدف الجهة، ثم الموحد لكل كلية، ثم الجامعة لما لا يتغير بحجم الجهة.</summary>
    private static (TargetInfo Info, IndicatorTarget? Row) Resolve(IEnumerable<IndicatorTarget> source, OrgUnit unit, Guid universityId, int year, Aggregation aggregation)
    {
        var targets = source as IReadOnlyCollection<IndicatorTarget> ?? source.ToList();
        var own = Latest(targets.Where(t => t.UnitId == unit.Id && t.Kind == TargetKind.Target), year);
        if (own is not null) return (new(own.Value, TargetSource.Unit, own.Year, false), own);
        if (unit.Kind == OrgUnitKind.University) return (TargetInfo.None, null);

        // الجهة المركزية تدخل رقم الجامعة كلها (مؤشر مركزي أو عدد بعد إزالة التكرار)، فمستهدفها مستهدف الجامعة.
        if (!unit.IsCollegeLevel && unit.Kind != OrgUnitKind.Department)
        {
            var whole = Latest(targets.Where(t => t.UnitId == universityId && t.Kind == TargetKind.Target), year);
            return whole?.Value is not null ? (new(whole.Value, TargetSource.University, whole.Year, false), whole) : (TargetInfo.None, null);
        }

        var each = Latest(targets.Where(t => t.Kind == TargetKind.EachCollege), year);
        if (each?.Value is not null) return (new(each.Value, TargetSource.EachCollege, each.Year, false), each);

        // ADR-0017: the college is measured against its own targets only; the university's is shown beside them, not used.
        if (unit.Kind == OrgUnitKind.Department && ComparableAcrossLevels(aggregation))
        {
            var uni = Latest(targets.Where(t => t.UnitId == universityId && t.Kind == TargetKind.Target), year);
            if (uni?.Value is not null) return (new(uni.Value, TargetSource.University, uni.Year, false), uni);
        }
        return (TargetInfo.None, null);
    }

    /// <summary>
    /// عمود المستهدف بجانب القيمة: مستهدف الجهة نفسها، ثم الموحد لكل كلية (للكلية والقسم)، ثم مستهدف الجامعة. المعتمد وحده،
    /// والنصي يعرض كما كتب. يختلف عن <see cref="Resolve"/> في أمرين: يعرض النص، ويعرض مستهدف الجامعة حين لا مستهدف للجهة.
    /// </summary>
    private static ShownTarget Show(IReadOnlyCollection<IndicatorTarget> approved, OrgUnit unit, Guid universityId, int year)
    {
        var own = Latest(approved.Where(t => t.UnitId == unit.Id && t.Kind == TargetKind.Target), year);
        if (own is not null) return new(own.Value, own.Text, TargetSource.Unit);
        if (unit.Kind == OrgUnitKind.University) return ShownTarget.None;
        if (unit.IsCollegeLevel || unit.Kind == OrgUnitKind.Department)
        {
            var each = Latest(approved.Where(t => t.Kind == TargetKind.EachCollege), year);
            if (each is not null) return new(each.Value, each.Text, TargetSource.EachCollege);
        }
        var uni = Latest(approved.Where(t => t.UnitId == universityId && t.Kind == TargetKind.Target), year);
        return uni is null ? ShownTarget.None : new(uni.Value, uni.Text, TargetSource.University);
    }

    /// <summary>مستهدف الجامعة الساري المعتمد، للمقارنة بجانب مستهدف الكلية.</summary>
    public static decimal? University(IReadOnlyCollection<IndicatorTarget> targets, Guid universityId, int year, int? planStart) =>
        planStart is { } s && year < s ? null : Latest(targets.Where(t => IsApproved(t) && t.UnitId == universityId && t.Kind == TargetKind.Target), year)?.Value;

    private static IndicatorTarget? Latest(IEnumerable<IndicatorTarget> rows, int year) =>
        rows.Where(t => t.Year <= year && (t.Value is not null || t.Text is not null)).OrderByDescending(t => t.Year).FirstOrDefault();
}
