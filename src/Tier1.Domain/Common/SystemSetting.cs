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

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(NearTargetPercent, 90, 50, 99, "حد «قريب من المستهدف» (اللون الأصفر)", "Near-target threshold (amber)", "% من المستهدف", "% of target"),
        new(BigChangePercent, 50, 10, 500, "حد تحذير الاختلاف عن آخر قيمة معتمدة", "Large-change warning threshold", "%", "%"),
        new(ReminderDaysBeforeDue, 3, 1, 14, "بدء تذكير الجهات قبل الموعد النهائي", "Start reminders before the deadline", "يوم", "days"),
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
