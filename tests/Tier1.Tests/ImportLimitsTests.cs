using System.IO.Compression;
using ClosedXML.Excel;
using Tier1.Application.Abstractions;
using Tier1.Domain.Common;
using Tier1.Infrastructure.Files;

namespace Tier1.Tests;

/// <summary>SEC-1: an uploaded workbook is checked by what it inflates to, and by its width, before its cells are read.</summary>
public class ImportLimitsTests
{
    private static MemoryStream Zip(params (string Name, long Bytes)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                var zeros = new byte[1024 * 1024];
                for (long left = bytes; left > 0; left -= zeros.Length) s.Write(zeros, 0, (int)Math.Min(zeros.Length, left));
            }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void A_small_file_that_inflates_past_the_cap_is_refused()
    {
        using var bomb = Zip(("xl/worksheets/sheet1.xml", 64L * 1024 * 1024));
        Assert.True(bomb.Length < 1024 * 1024, "The bomb itself is small");
        var e = Assert.Throws<DomainException>(() => ZipGuard.Check(bomb));
        Assert.Equal(("excel_too_large", 400), (e.Code, e.Status));
    }

    [Fact]
    public void Many_parts_together_past_the_total_cap_are_refused()
    {
        using var parts = Zip(Enumerable.Range(0, 5).Select(i => ($"xl/part{i}.xml", 10L * 1024 * 1024)).ToArray());
        Assert.Equal("excel_too_large", Assert.Throws<DomainException>(() => ZipGuard.Check(parts)).Code);
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_an_invalid_workbook()
    {
        using var text = new MemoryStream("not a workbook"u8.ToArray());
        Assert.Equal("invalid_excel", Assert.Throws<DomainException>(() => new ClosedXmlSpreadsheets().Read(text)).Code);
    }

    [Fact]
    public void The_downloaded_template_shape_reads()
    {
        var sheets = new ClosedXmlSpreadsheets();
        var bytes = sheets.Write(new Sheet("النموذج", ["الكود", "القيمة"], [["1.01", "12"], ["1.02", "80"]]));
        var (headers, rows) = sheets.Read(new MemoryStream(bytes));
        Assert.Equal(["الكود", "القيمة"], headers);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void A_sheet_wider_than_the_limit_is_refused()
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add("s");
        ws.Cell(1, 1).Value = "الكود";
        ws.Cell(2, ClosedXmlSpreadsheets.MaxColumns + 5).Value = "x";
        using var ms = new MemoryStream();
        book.SaveAs(ms);
        ms.Position = 0;
        Assert.Equal("too_many_columns", Assert.Throws<DomainException>(() => new ClosedXmlSpreadsheets().Read(ms)).Code);
    }
}
