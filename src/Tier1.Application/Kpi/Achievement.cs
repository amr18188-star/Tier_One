using Tier1.Domain.Kpi;

namespace Tier1.Application.Kpi;

/// <summary>
/// نسبة الإنجاز من المستهدف، مصدر واحد للنتائج وبطاقة الأداء (D7، ADR-0006). دالة نقية تعيد النسبة الدقيقة (1 = 100%) بلا حد ولا تقريب؛
/// والعرض على كل شاشة: النتائج تحكم بها كما هي (120% تبقى 120%)، وبطاقة الأداء تحدها بـ100% وتقربها لرقم عشري كما في ملف الإطار.
/// <list type="bullet">
/// <item><b>الأعلى أفضل</b>: الفعلي ÷ المستهدف. المستهدف صفر: محقق إن كانت القيمة صفرًا أو أكثر (2.02 نمو التسجيل).</item>
/// <item><b>الأقل أفضل</b>: المستهدف ÷ الفعلي، والفعلي صفر محقق. المستهدف صفر: (100 − القيمة) ÷ 100 بين صفر وواحد، كما في ملف الإطار.</item>
/// <item><b>الوصول إلى قيمة</b>: 1 − نسبة الانحراف عن المستهدف في أي اتجاه، ولا تقل عن صفر (calculation-review.md).
/// المستهدف صفر: محقق إن كانت القيمة صفرًا.</item>
/// <item>اتجاه غير محدد، أو بلا قيمة، أو بلا مستهدف: null (لا يحكم عليه).</item>
/// </list>
/// </summary>
public static class Achievement
{
    public static decimal? Ratio(Direction direction, decimal? actual, decimal? target)
    {
        if (actual is not { } a || target is not { } t) return null;
        return direction switch
        {
            Direction.HigherIsBetter => t == 0 ? (a >= 0 ? 1 : 0) : a / t,
            Direction.LowerIsBetter => t == 0 ? Math.Max(0, Math.Min(100 - a, 100)) / 100 : a == 0 ? 1 : t / a,
            Direction.Range => t == 0 ? (a == 0 ? 1 : 0) : Math.Max(0, 1 - Math.Abs(a - t) / Math.Abs(t)),
            _ => null,
        };
    }
}
