namespace Tier1.Domain.Common;

/// <summary>إعداد تشغيلي يضبطه المدير من «إعدادات النظام» (adm.settings)؛ ما لم يحفظ يأخذ قيمته الافتراضية من SettingKeys.</summary>
public sealed class SystemSetting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>تعريف إعداد: قيمته الافتراضية وحدوده واسمه.</summary>
public sealed record SettingDefinition(string Key, int Default, int Min, int Max, string Ar, string En, string UnitAr, string UnitEn);

public static class SettingKeys
{
    /// <summary>حد «قريب من المستهدف» في ألوان الأداء: نسبة الإنجاز من المستهدف التي تعد أصفر لا أحمر.</summary>
    public const string NearTargetPercent = "near_target_percent";

    /// <summary>حد تحذير الاختلاف الكبير عن آخر قيمة معتمدة؛ يطلب تفسيرًا ولا يرفض القيمة.</summary>
    public const string BigChangePercent = "big_change_percent";

    /// <summary>كم يومًا قبل الموعد النهائي يبدأ تذكير الجهات المتأخرة بالبريد.</summary>
    public const string ReminderDaysBeforeDue = "reminder_days_before_due";

    /// <summary>سنة بداية الربع الأول من السنة الأولى للخطة (السنة الأولى 2027).</summary>
    public const string PlanStartYear = "plan_q1_start_year";

    /// <summary>شهر بداية الربع الأول من السنة الأولى؛ كل ربع ثلاثة أشهر بعده.</summary>
    public const string PlanStartMonth = "plan_q1_start_month";

    /// <summary>الحد الأدنى لدرجة الركيزة في كل فئة (جدول حدود تصنيف الركائز)؛ الفئة 5 ما دون الفئة 4.</summary>
    public const string Tier1Min = "tier1_min";
    public const string Tier2Min = "tier2_min";
    public const string Tier3Min = "tier3_min";
    public const string Tier4Min = "tier4_min";

    /// <summary>كم تزيد حدود الدرجة الكلية على حدود الركائز (+5 في الإطار).</summary>
    public const string OverallTierOffset = "overall_tier_offset";

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(NearTargetPercent, 90, 50, 99, "حد «قريب من المستهدف» (اللون الأصفر)", "Near-target threshold (amber)", "% من المستهدف", "% of target"),
        new(BigChangePercent, 50, 10, 500, "حد تحذير الاختلاف عن آخر قيمة معتمدة", "Large-change warning threshold", "%", "%"),
        new(ReminderDaysBeforeDue, 3, 1, 14, "بدء تذكير الجهات قبل الموعد النهائي", "Start reminders before the deadline", "يوم", "days"),
        new(PlanStartYear, 2026, 2024, 2030, "سنة بداية الربع الأول من السنة الأولى للخطة", "Year in which quarter 1 of plan year 1 starts", "سنة ميلادية", "year"),
        new(PlanStartMonth, 9, 1, 12, "شهر بداية الربع الأول من السنة الأولى للخطة", "Month in which quarter 1 of plan year 1 starts", "رقم الشهر (1–12)", "month (1-12)"),
        new(Tier1Min, 85, 50, 100, "الفئة 1 (جامعة بحثية عالمية): أدنى درجة للركيزة", "Tier 1 minimum pillar score", "من 100", "of 100"),
        new(Tier2Min, 70, 30, 99, "الفئة 2 (جامعة وطنية للبحث والابتكار): أدنى درجة للركيزة", "Tier 2 minimum pillar score", "من 100", "of 100"),
        new(Tier3Min, 55, 20, 98, "الفئة 3 (جامعة شاملة): أدنى درجة للركيزة", "Tier 3 minimum pillar score", "من 100", "of 100"),
        new(Tier4Min, 40, 10, 97, "الفئة 4 (جامعة إقليمية تعليمية): أدنى درجة للركيزة", "Tier 4 minimum pillar score", "من 100", "of 100"),
        new(OverallTierOffset, 5, 0, 15, "زيادة حدود الدرجة الكلية على حدود الركائز", "Overall-score thresholds raised by", "نقطة", "points"),
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
