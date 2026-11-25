namespace Tier1.Application.Abstractions;

/// <summary>ورقة Excel بسيطة: صف عناوين ثم صفوف نصية. اتجاه الورقة من اليمين لليسار للعربية.</summary>
public sealed record Sheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string?>> Rows, bool RightToLeft = true);

/// <summary>كتابة كشوف Excel وقراءة ما يرفعه الموظف (D16). الملف المرفوع يقرأ منه الورقة الأولى فقط، خلاياه نصًا.</summary>
public interface ISpreadsheets
{
    byte[] Write(Sheet sheet);

    /// <summary>صف العناوين ثم الصفوف؛ يرمي DomainException إن لم يكن الملف Excel صالحًا أو تجاوز الحد.</summary>
    (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) Read(Stream content, int maxRows = 2000);
}

/// <summary>صف مؤشر في ملف الإطار: قيم الأرباع العشرين، ومستهدفات السنوات الخمس (الخامسة هي المستهدف النهائي)، ونمطه ونوعه ووحدته.</summary>
public sealed record KpiRow(IReadOnlyList<decimal?> Quarters, IReadOnlyList<decimal?> YearTargets, bool LowerIsBetter, bool Cumulative, string Unit);

/// <summary>
/// ملف «الإطار الشامل لتقييم أداء الجامعات» (KPI.xlsx) نفسه بقيم الجامعة الربعية في خلاياه الزرقاء (ر1–ر20)، وبالمستهدفات والنمط والنوع والوحدة
/// كما في البرنامج (ومنها التصحيحات المعتمدة)، بصيغه وتنسيقه وأوراقه كما هي، ليعاد حسابه عند فتحه فتطابق نتائجه البرنامج.
/// </summary>
public interface IKpiWorkbook
{
    /// <summary>rows: كود المؤشر (1.01…) ← صفه.</summary>
    byte[] Fill(IReadOnlyDictionary<string, KpiRow> rows);

    /// <summary>G-10: the indicator codes the council's workbook has a row for. Any other indicator is not in the file.</summary>
    IReadOnlyCollection<string> Codes { get; }
}
