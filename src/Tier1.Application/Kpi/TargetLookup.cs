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

    /// <summary>مستهدف الجامعة: لمؤشر نسبة أو متوسط بلا مستهدف خاص بالكلية، فتعرف الكلية بعدها عن نسبة الجامعة المرجوة.</summary>
    University = 3,
}

/// <summary>
/// المستهدف الساري لجهة في سنة: القيمة ومصدرها وسنته، أو «لم يبدأ» قبل سنة بدء المؤشر في الخطة.
/// PendingValue وPendingYear: مستهدف مقترح كان سيسري لو اعتمد (D3)؛ للعرض «بانتظار الاعتماد» فقط، ولا يدخل أي حساب.
/// </summary>
public sealed record TargetInfo(decimal? Value, TargetSource Source, int? Year, bool NotStarted, decimal? PendingValue = null, int? PendingYear = null)
{
    public bool HasPending => PendingYear is not null;

    public static readonly TargetInfo None = new(null, TargetSource.None, null, false);
}

/// <summary>
/// المستهدف الساري (دالة نقية)، بالقواعد نفسها في النماذج والنتائج وبطاقة الأداء والتقدم:
/// **المعتمد وحده** يقاس عليه الأداء (D3)؛ فالمقترح لا يغير مقياس جهة قبل أن تعتمده اللجنة العليا، ويسري آخر معتمد قبله.
/// قبل سنة بدء المؤشر «لم يبدأ»؛ وفي السنة نفسها مستهدفها، وإلا آخر مستهدف محدد قبلها (يستمر المستهدف بعد سنة تحقيقه)؛
/// والكلية بلا مستهدف خاص يسري عليها الموحد لكل كلية، ثم مستهدف الجامعة إن كان المؤشر نسبة أو متوسطًا لا يجمع.
/// المجموع لا يقارن بمستهدف الجامعة، لأن الجامعة مجموع الكليات.
/// </summary>
public static class TargetLookup
{
    /// <summary>مؤشرات قيمتها لا تتغير بحجم الجهة (نسبة، معدل، متوسط)، فتقارن الكلية بمستهدف الجامعة نفسه.</summary>
    public static bool ComparableAcrossLevels(Aggregation aggregation) => aggregation is Aggregation.RatioOfSums or Aggregation.WeightedAverage;

    public static TargetInfo For(IReadOnlyCollection<IndicatorTarget> targets, OrgUnit unit, Guid universityId, int year, int? planStart, Aggregation aggregation)
    {
        if (planStart is { } start && year < start) return new(null, TargetSource.None, start, true);
        var (info, row) = Resolve(targets.Where(IsApproved), unit, universityId, year, aggregation);
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

        if (ComparableAcrossLevels(aggregation))
        {
            var uni = Latest(targets.Where(t => t.UnitId == universityId && t.Kind == TargetKind.Target), year);
            if (uni?.Value is not null) return (new(uni.Value, TargetSource.University, uni.Year, false), uni);
        }
        return (TargetInfo.None, null);
    }

    /// <summary>مستهدف الجامعة الساري المعتمد، للمقارنة بجانب مستهدف الكلية.</summary>
    public static decimal? University(IReadOnlyCollection<IndicatorTarget> targets, Guid universityId, int year, int? planStart) =>
        planStart is { } s && year < s ? null : Latest(targets.Where(t => IsApproved(t) && t.UnitId == universityId && t.Kind == TargetKind.Target), year)?.Value;

    private static IndicatorTarget? Latest(IEnumerable<IndicatorTarget> rows, int year) =>
        rows.Where(t => t.Year <= year && (t.Value is not null || t.Text is not null)).OrderByDescending(t => t.Year).FirstOrDefault();
}
