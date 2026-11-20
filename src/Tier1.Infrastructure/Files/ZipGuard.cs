using System.IO.Compression;
using Tier1.Domain.Common;

namespace Tier1.Infrastructure.Files;

/// <summary>
/// SEC-1: an uploaded .xlsx is a zip. Before ClosedXML loads it whole, every part is inflated here against hard caps, counting
/// the bytes actually produced (the sizes a zip declares can be forged), so a small file that inflates to gigabytes is refused
/// after reading at most the cap instead of exhausting the server's memory.
/// </summary>
public static class ZipGuard
{
    /// <summary>Caps for one uploaded workbook: generous for a form of a few hundred rows, far below what hurts the server.</summary>
    public sealed record Limits(int MaxEntries = 200, long MaxEntryBytes = 20 * 1024 * 1024, long MaxTotalBytes = 40 * 1024 * 1024);

    public static readonly Limits Default = new();

    /// <summary>Returns a seekable copy of the content checked against the limits, or throws a 400 DomainException.</summary>
    public static MemoryStream Check(Stream content, Limits? limits = null)
    {
        var l = limits ?? Default;
        var copy = new MemoryStream();
        content.CopyTo(copy);
        copy.Position = 0;
        try
        {
            using var zip = new ZipArchive(copy, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > l.MaxEntries) throw TooLarge();
            var buffer = new byte[81920];
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                using var s = entry.Open();
                long size = 0;
                int read;
                while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    size += read;
                    total += read;
                    if (size > l.MaxEntryBytes || total > l.MaxTotalBytes) throw TooLarge();
                }
            }
        }
        catch (InvalidDataException)
        {
            throw new DomainException("invalid_excel", "الملف ليس ملف Excel صالحًا (xlsx).", "The file is not a valid Excel (xlsx) file.", 400);
        }
        copy.Position = 0;
        return copy;
    }

    private static DomainException TooLarge() =>
        new("excel_too_large", "محتوى الملف أكبر مما يقبله الاستيراد. استخدم قالب النموذج كما نزل، بلا أوراق أو أعمدة إضافية.",
            "The file's content is larger than the import accepts. Use the form template as downloaded.", 400);
}
