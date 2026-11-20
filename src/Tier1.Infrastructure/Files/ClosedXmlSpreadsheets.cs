using ClosedXML.Excel;
using Tier1.Application.Abstractions;
using Tier1.Domain.Common;

namespace Tier1.Infrastructure.Files;

/// <summary>كشوف Excel بمكتبة ClosedXML: عناوين بارزة ثابتة، وأعمدة بعرض محتواها، وكل الخلايا نصًا حتى لا تفقد الأكواد أصفارها.</summary>
public sealed class ClosedXmlSpreadsheets : ISpreadsheets
{
    public byte[] Write(Sheet sheet)
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add(sheet.Name.Length > 31 ? sheet.Name[..31] : sheet.Name);
        ws.RightToLeft = sheet.RightToLeft;
        for (var c = 0; c < sheet.Headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = sheet.Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F6F6");
        }
        for (var r = 0; r < sheet.Rows.Count; r++)
            for (var c = 0; c < sheet.Rows[r].Count; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                cell.Style.NumberFormat.Format = "@";
                cell.SetValue(sheet.Rows[r][c] ?? "");
            }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(8.0, 60.0);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>SEC-1: an upload has a handful of columns; anything wider is refused before reading its cells.</summary>
    public const int MaxColumns = 60;

    public (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) Read(Stream content, int maxRows = 2000)
    {
        // SEC-1: the zip is inflated against hard caps first, so a zip bomb is refused before ClosedXML loads it whole.
        using var checkedContent = ZipGuard.Check(content);
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(checkedContent);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DomainException("invalid_excel", "الملف ليس ملف Excel صالحًا (xlsx).", "The file is not a valid Excel (xlsx) file.", 400);
        }
        using (book)
        {
            var ws = book.Worksheets.First();
            var used = ws.RangeUsed();
            if (used is null) return ([], []);
            var lastCol = used.LastColumn().ColumnNumber();
            var lastRow = used.LastRow().RowNumber();
            if (lastRow - 1 > maxRows) throw new DomainException("too_many_rows", $"الملف أكثر من {maxRows} صف.", $"The file has more than {maxRows} rows.", 400);
            if (lastCol > MaxColumns) throw new DomainException("too_many_columns", $"الملف أكثر من {MaxColumns} عمودًا؛ استخدم قالب النموذج كما نزل.", $"The file has more than {MaxColumns} columns.", 400);
            var headers = Enumerable.Range(1, lastCol).Select(c => ws.Cell(1, c).GetFormattedString().Trim()).ToList();
            var rows = new List<IReadOnlyList<string>>();
            for (var r = 2; r <= lastRow; r++)
            {
                var row = Enumerable.Range(1, lastCol).Select(c => ws.Cell(r, c).GetFormattedString().Trim()).ToList();
                if (row.All(string.IsNullOrEmpty)) continue;
                rows.Add(row);
            }
            return (headers, rows);
        }
    }
}
