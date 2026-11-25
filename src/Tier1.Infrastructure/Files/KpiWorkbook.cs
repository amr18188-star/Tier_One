using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Files;

/// <summary>
/// يملأ قالب KPI.xlsx (الملف الأصلي مضمنًا في البرنامج) بقيم الأرباع: يعدل خلايا ر1–ر20 (U إلى AN) في صفوف المؤشرات فقط
/// داخل ملف XML للورقة، ولا يمس الصيغ ولا التنسيق ولا الأوراق الأخرى ولا الرسوم، ثم يطلب من Excel إعادة الحساب عند الفتح.
/// صف كل مؤشر من بذرة الإطار (kpi-framework.json) التي استخرجت من الملف نفسه.
/// </summary>
public sealed class KpiWorkbook : IKpiWorkbook
{
    private const string CardSheet = "📊 بطاقة مؤشرات الأداء";
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Rows = new(LoadRows);

    public IReadOnlyCollection<string> Codes => [.. Rows.Value.Keys];

    public byte[] Fill(IReadOnlyDictionary<string, KpiRow> rows)
    {
        var buffer = new MemoryStream();
        using (var template = Resource("Tier1.Infrastructure.Templates.KPI.xlsx")) template.CopyTo(buffer);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var sheetPath = SheetPath(zip, CardSheet);
            var entry = zip.GetEntry(sheetPath) ?? throw new InvalidOperationException($"{sheetPath} is missing from the template.");
            XDocument sheet;
            using (var s = entry.Open()) sheet = XDocument.Load(s, LoadOptions.PreserveWhitespace);
            var data = sheet.Root!.Element(Main + "sheetData")!;
            foreach (var (code, row) in Rows.Value)
            {
                if (rows.GetValueOrDefault(code) is not { } r) continue;
                for (var k = 0; k < 20; k++) SetCell(data, row, ColumnIndex("U") + k, r.Quarters.Count > k ? r.Quarters[k] : null);
                // المستهدف النهائي (T) ومستهدفات السنوات 1–4 (CD–CG)، والنمط (CC) والنوع (CB) والوحدة (P) كما في البرنامج.
                SetCell(data, row, ColumnIndex("T"), r.YearTargets.Count > 4 ? r.YearTargets[4] : null);
                for (var k = 0; k < 4; k++) SetCell(data, row, ColumnIndex("CD") + k, r.YearTargets.Count > k ? r.YearTargets[k] : null);
                SetText(data, row, ColumnIndex("CC"), r.LowerIsBetter ? "تنازلى (الأقل أفضل)" : "تصاعدى (الأعلى أفضل)");
                SetText(data, row, ColumnIndex("CB"), r.Cumulative ? "مؤشر تراكمي\n(Cumulative)" : "مؤشر غير تراكمي (لحظي)\n(Non-cumulative)");
                SetText(data, row, ColumnIndex("P"), r.Unit);
            }
            Replace(zip, sheetPath, sheet);

            // إعادة الحساب الكاملة عند الفتح: القيم المحسوبة المخزنة في القالب قديمة بعد تغيير المدخلات.
            var workbook = Load(zip, "xl/workbook.xml");
            var calc = workbook.Root!.Element(Main + "calcPr");
            if (calc is null)
            {
                calc = new XElement(Main + "calcPr");
                workbook.Root.Add(calc);
            }
            calc.SetAttributeValue("fullCalcOnLoad", "1");
            Replace(zip, "xl/workbook.xml", workbook);
        }
        return buffer.ToArray();
    }

    /// <summary>يضع نصًا في الخلية (نصًا مضمنًا) مع الإبقاء على تنسيقها.</summary>
    private static void SetText(XElement sheetData, int row, int column, string text)
    {
        var cell = Cell(sheetData, row, column, create: true)!;
        cell.Elements().Remove();
        cell.SetAttributeValue("t", "inlineStr");
        cell.Add(new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }

    /// <summary>يضع قيمة رقمية في الخلية (أو يفرغها) مع الإبقاء على تنسيقها.</summary>
    private static void SetCell(XElement sheetData, int row, int column, decimal? value)
    {
        var cell = Cell(sheetData, row, column, create: value is not null);
        if (cell is null) return;
        // خلية إدخال: لا صيغة فيها؛ يزال النوع والقيمة القديمة ويبقى التنسيق (s).
        cell.Attribute("t")?.Remove();
        cell.Elements().Remove();
        if (value is { } v) cell.Add(new XElement(Main + "v", v.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>الخلية في صفها، أو تضاف في موضعها بترتيب الأعمدة إن طلب ذلك.</summary>
    private static XElement? Cell(XElement sheetData, int row, int column, bool create)
    {
        var rowEl = sheetData.Elements(Main + "row").FirstOrDefault(r => (int?)r.Attribute("r") == row);
        if (rowEl is null)
        {
            if (!create) return null;
            rowEl = new XElement(Main + "row", new XAttribute("r", row));
            var after = sheetData.Elements(Main + "row").LastOrDefault(r => (int)r.Attribute("r")! < row);
            if (after is null) sheetData.AddFirst(rowEl);
            else after.AddAfterSelf(rowEl);
        }
        var reference = ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
        var cell = rowEl.Elements(Main + "c").FirstOrDefault(c => (string?)c.Attribute("r") == reference);
        if (cell is null)
        {
            if (!create) return null;
            cell = new XElement(Main + "c", new XAttribute("r", reference));
            var before = rowEl.Elements(Main + "c").FirstOrDefault(c => ColumnIndex(Letters((string)c.Attribute("r")!)) > column);
            if (before is null) rowEl.Add(cell);
            else before.AddBeforeSelf(cell);
        }
        return cell;
    }

    private static string SheetPath(ZipArchive zip, string name)
    {
        var workbook = Load(zip, "xl/workbook.xml");
        var id = workbook.Root!.Element(Main + "sheets")!.Elements(Main + "sheet").First(s => (string?)s.Attribute("name") == name).Attribute(Rel + "id")!.Value;
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var target = rels.Root!.Elements(PkgRel + "Relationship").First(r => (string?)r.Attribute("Id") == id).Attribute("Target")!.Value;
        return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
    }

    private static XDocument Load(ZipArchive zip, string path)
    {
        using var s = (zip.GetEntry(path) ?? throw new InvalidOperationException($"{path} is missing.")).Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    private static void Replace(ZipArchive zip, string path, XDocument doc)
    {
        zip.GetEntry(path)!.Delete();
        using var s = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        doc.Save(s, SaveOptions.DisableFormatting);
    }

    private static int ColumnIndex(string letters) => letters.Aggregate(0, (n, ch) => n * 26 + (ch - 'A' + 1));

    private static string Letters(string reference) => new(reference.TakeWhile(char.IsLetter).ToArray());

    private static string ColumnName(int index)
    {
        var name = "";
        while (index > 0)
        {
            var m = (index - 1) % 26;
            name = (char)('A' + m) + name;
            index = (index - m) / 26;
        }
        return name;
    }

    private static Stream Resource(string name) =>
        typeof(KpiWorkbook).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");

    private static IReadOnlyDictionary<string, int> LoadRows()
    {
        using var s = Resource("Tier1.Infrastructure.Persistence.Seed.kpi-framework.json");
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.GetProperty("indicators").EnumerateArray()
            .ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("row").GetInt32());
    }
}
