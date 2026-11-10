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
    /// <summary>مدة حجز المقعد المؤقت لطلب من البوابة قبل السداد (D12).</summary>
    public const string SeatHoldHours = "seat_hold_hours";

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(SeatHoldHours, 48, 1, 168, "مهلة سداد المقعد المحجوز مؤقتًا", "Payment window for a temporarily held seat", "ساعة", "hours"),
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
