namespace Tier1.Domain.Calendar;

/// <summary>إجازة رسمية في تقويم العمل (adm.calendar)؛ تستبعد من مهل الاسترداد وتغيير الموعد وإنجاز الترجمة.</summary>
public sealed class Holiday
{
    public DateOnly Date { get; set; }

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
