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
