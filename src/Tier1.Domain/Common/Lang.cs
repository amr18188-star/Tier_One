using System.Globalization;

namespace Tier1.Domain.Common;

/// <summary>اختيار النص حسب لغة الطلب الحالية (العربية افتراضيًا).</summary>
public static class Lang
{
    public static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    public static string Pick(string ar, string en) => IsEnglish ? en : ar;

    /// <summary>اسم ثنائي اللغة: الإنجليزي إن طلب ووجد، وإلا العربي.</summary>
    public static string Name(string ar, string? en) => IsEnglish && !string.IsNullOrWhiteSpace(en) ? en : ar;
}

/// <summary>نص بلغتين؛ الإنجليزي اختياري ويعود للعربي إن غاب.</summary>
public sealed record BiText(string Ar, string? En)
{
    public string Pick() => Lang.Name(Ar, En);
}

/// <summary>توقيت القاهرة للعرض والمهل؛ التخزين UTC دائمًا (BR07، NFR09).</summary>
public static class CairoTime
{
    public static readonly TimeZoneInfo Zone = FindZone();

    public static DateTimeOffset ToCairo(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static DateOnly DateOf(DateTimeOffset utc) => DateOnly.FromDateTime(ToCairo(utc).DateTime);

    /// <summary>بداية يوم القاهرة بتوقيت UTC.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>يوم ووقت بتوقيت القاهرة بتوقيت UTC.</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>هل وجدت بيانات منطقة القاهرة الزمنية؛ بدونها تنحرف المهل والمواعيد ساعتين أو ثلاثًا، فيرفض التشغيل خارج التطوير.</summary>
    public static bool ZoneFound => Zone != TimeZoneInfo.Utc;

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        }
        return TimeZoneInfo.Utc;
    }
}
