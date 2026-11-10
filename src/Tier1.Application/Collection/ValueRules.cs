using Tier1.Domain.Collection;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Collection;

public enum IssueSeverity
{
    /// <summary>يمنع الحفظ أو الإرسال.</summary>
    Error = 1,

    /// <summary>يسمح بالإرسال مع تفسير مكتوب.</summary>
    Warning = 2,
}

/// <summary>مشكلة في قيمة: رمز ثابت ورسالة تقول المشكلة وطريقة تصحيحها.</summary>
public sealed record ValueIssue(string Code, IssueSeverity Severity, string MessageAr);

/// <summary>ما يلزم من تعريف المؤشر للتحقق من قيمته (من لقطة الدورة).</summary>
public sealed record ValueSpec(string Unit, Aggregation Aggregation, bool EvidenceRequired);

public sealed record ValueInput(ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, string? Reason, string? Explanation);

/// <summary>
/// التحقق من قيمة مؤشر (دالة نقية تعمل على كل طرق الإدخال: الشاشة وExcel والتكامل).
/// الأخطاء المؤكدة تمنع، والتحذير لا يرفض الرقم لمجرد اختلافه عن العام السابق بل يطلب تفسيرًا.
/// </summary>
public static class ValueRules
{
    /// <summary>وحدات لا تقبل إلا أعدادًا صحيحة.</summary>
    private static readonly HashSet<string> IntegerUnits = new(StringComparer.Ordinal) { "عدد", "برنامج", "دولة", "سرير", "مركز" };

    public const decimal BigChange = 0.5m;

    public static bool IsPercent(ValueSpec spec) => spec.Unit.Trim() == "%";

    public static bool UsesRatio(ValueSpec spec) => spec.Aggregation == Aggregation.RatioOfSums;

    /// <summary>
    /// يتحقق ويعيد القيمة التي تحفظ: للنسبة تحسب من البسط والمقام. previous آخر قيمة معتمدة للجهة نفسها إن وجدت.
    /// </summary>
    public static (decimal? Value, IReadOnlyList<ValueIssue> Issues) Check(ValueSpec spec, ValueInput input, decimal? previous, decimal bigChange = BigChange)
    {
        var issues = new List<ValueIssue>();
        if (input.Kind != ValueKind.Number)
        {
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length < 5)
                issues.Add(new("reason_required", IssueSeverity.Error,
                    input.Kind == ValueKind.NotAvailable ? "اكتب سبب عدم إتاحة القيمة (5 أحرف على الأقل)." : "اكتب سبب عدم انطباق المؤشر على الجهة (5 أحرف على الأقل)."));
            return (null, issues);
        }

        decimal? value;
        if (UsesRatio(spec))
        {
            if (input.Numerator is not { } n || input.Denominator is not { } d)
            {
                issues.Add(new("numerator_denominator", IssueSeverity.Error, "أدخل البسط والمقام؛ النسبة تحسب منهما تلقائيًا."));
                return (null, issues);
            }
            if (n < 0) issues.Add(new("negative", IssueSeverity.Error, "البسط لا يكون سالبًا."));
            if (d <= 0) issues.Add(new("denominator_zero", IssueSeverity.Error, "المقام أكبر من صفر؛ إن لم يوجد ما يقاس اختر «غير منطبق» واكتب السبب."));
            if (IsPercent(spec) && n > d)
                issues.Add(new("numerator_exceeds", IssueSeverity.Error, $"البسط {Fmt(n)} أكبر من المقام {Fmt(d)}؛ راجع إحدى القيمتين."));
            if (issues.Count > 0) return (null, issues);
            value = decimal.Round(n / d * (IsPercent(spec) ? 100 : 1), 4);
        }
        else
        {
            if (input.Value is not { } v)
            {
                issues.Add(new("value_required", IssueSeverity.Error, "أدخل القيمة، أو اختر «غير متاح» أو «غير منطبق» مع السبب."));
                return (null, issues);
            }
            if (v < 0) issues.Add(new("negative", IssueSeverity.Error, "القيمة لا تكون سالبة."));
            if (IsPercent(spec) && v > 100) issues.Add(new("percent_range", IssueSeverity.Error, $"النسبة من 0 إلى 100؛ أدخلت {Fmt(v)}."));
            if (IntegerUnits.Contains(spec.Unit.Trim()) && v != decimal.Truncate(v))
                issues.Add(new("integer", IssueSeverity.Error, $"«{spec.Unit}» عدد صحيح بلا كسور؛ أدخلت {Fmt(v)}."));
            if (issues.Count > 0) return (null, issues);
            value = v;
        }

        if (previous is { } p && value is { } now && p != 0 && Math.Abs(now - p) / Math.Abs(p) > bigChange)
            issues.Add(new("big_change", IssueSeverity.Warning,
                $"القيمة {Fmt(now)} تختلف كثيرًا عن آخر قيمة معتمدة ({Fmt(p)}). تأكد منها واكتب تفسيرًا للمراجع."));
        return (value, issues);
    }

    /// <summary>
    /// فحوص الإرسال: كل مؤشر له قيمة أو حالة، والدليل المطلوب مرفق، وكل تحذير مفسر. تعيد ما يمنع الإرسال.
    /// </summary>
    public static IReadOnlyList<(Guid IndicatorId, ValueIssue Issue)> ReadyToSubmit(
        IReadOnlyList<(Guid IndicatorId, string Code, ValueSpec Spec)> required,
        IReadOnlyDictionary<Guid, SubmittedValue> values,
        IReadOnlyDictionary<Guid, int> evidenceCount,
        IReadOnlyDictionary<Guid, decimal?> previous,
        decimal bigChange = BigChange)
    {
        var blocking = new List<(Guid, ValueIssue)>();
        foreach (var (id, code, spec) in required)
        {
            if (!values.TryGetValue(id, out var v))
            {
                blocking.Add((id, new("missing", IssueSeverity.Error, $"{code}: لم تدخل قيمته بعد.")));
                continue;
            }
            var (_, issues) = Check(spec, new ValueInput(v.Kind, v.Value, v.Numerator, v.Denominator, v.Reason, v.Explanation), previous.GetValueOrDefault(id), bigChange);
            foreach (var e in issues.Where(i => i.Severity == IssueSeverity.Error)) blocking.Add((id, e with { MessageAr = $"{code}: {e.MessageAr}" }));
            if (issues.Any(i => i.Severity == IssueSeverity.Warning) && string.IsNullOrWhiteSpace(v.Explanation))
                blocking.Add((id, new("explanation_required", IssueSeverity.Error, $"{code}: اكتب تفسيرًا للاختلاف الكبير عن العام السابق.")));
            if (spec.EvidenceRequired && v.Kind == ValueKind.Number && evidenceCount.GetValueOrDefault(id) == 0)
                blocking.Add((id, new("evidence_required", IssueSeverity.Error, $"{code}: أرفق الدليل المطلوب في تعريف المؤشر.")));
        }
        return blocking;
    }

    public static string Fmt(decimal v) => Kpi.DistributionRules.Fmt(v);
}
