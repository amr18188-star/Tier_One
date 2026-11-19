using System.Globalization;
using Tier1.Application.Abstractions;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;

namespace Tier1.Application.Collection;

public sealed record ImportRow(int Row, string Code, string? Name, string Status, string? Display, IReadOnlyList<IssueDto> Issues);

/// <summary>نتيجة المعاينة: لا يطبق شيء إلا إن خلت كل الصفوف من الأخطاء (الكل أو لا شيء).</summary>
public sealed record ImportPreview(int Rows, int Changes, int Errors, int Warnings, bool CanApply, IReadOnlyList<ImportRow> Items);

/// <summary>
/// إدخال النموذج من Excel: قالب خاص بكل نموذج فيه مؤشراته فقط، ثم معاينة كل صف بقواعد الإدخال نفسها، ثم تطبيق كامل أو لا شيء.
/// الخلية الفارغة لا تغير القيمة المحفوظة؛ والحالة «غير متاح» أو «لا ينطبق» تحتاج سببًا.
/// </summary>
public sealed partial class FormsService
{
    private static readonly string[] Columns =
        ["الرمز", "المؤشر", "المعيار", "الوحدة", "المستهدف", "القيمة السابقة", "الحالة", "القيمة", "البسط", "المقام", "السبب", "التفسير"];

    private const string NotAvailableText = "غير متاح";
    private const string NotApplicableText = "لا ينطبق";

    public async Task<(byte[] Content, string FileName)> TemplateAsync(Guid id, ISpreadsheets sheets, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.View, tracking: false, ct);
        var targets = await TargetsAsync(ctx, ct);
        // بترتيب النموذج نفسه: الركيزة ثم المعيار ثم المؤشر.
        var rows = ctx.Indicators.Select(i =>
        {
            ctx.Values.TryGetValue(i.Ci.IndicatorId, out var v);
            var ratio = ValueRules.UsesRatio(CycleSpec.Of(i.Version));
            return (IReadOnlyList<string?>)
            [
                i.Ci.Code, i.Version.NameAr, i.Standard.NameAr, i.Version.Unit,
                Num(targets.GetValueOrDefault(i.Ci.IndicatorId)?.Value), Num(ctx.Previous.GetValueOrDefault(i.Ci.IndicatorId)),
                v is null ? null : v.Kind switch { ValueKind.NotAvailable => NotAvailableText, ValueKind.NotApplicable => NotApplicableText, _ => "رقم" },
                ratio ? null : Num(v?.Value), Num(v?.Numerator), Num(v?.Denominator), v?.Reason, v?.Explanation,
            ];
        }).ToList();
        var bytes = sheets.Write(new Sheet("النموذج", Columns, rows));
        return (bytes, $"{ctx.Unit.Code}-{ctx.Cycle.PeriodLabel}.xlsx");
    }

    /// <summary>معاينة ملف مرفوع؛ apply=true يطبقه إن خلا من الأخطاء، في حفظ واحد.</summary>
    public async Task<ImportPreview> ImportAsync(Guid id, Stream content, ISpreadsheets sheets, bool apply, CancellationToken ct)
    {
        var ctx = await LoadAsync(id, Cap.Edit, tracking: apply, ct);
        EnsureEditable(ctx);
        var (headers, rows) = sheets.Read(content, 500);
        var col = Columns.ToDictionary(c => c, c => headers.ToList().FindIndex(h => h.Trim() == c));
        if (col["الرمز"] < 0 || (col["القيمة"] < 0 && col["البسط"] < 0))
            throw new DomainException("import_template", "الملف لا يطابق قالب النموذج. نزّل القالب من صفحة النموذج واملأه.", "The file does not match the form template.", 400);

        var bigChange = await settings.IntAsync(SettingKeys.BigChangePercent, ct) / 100m;
        var byCode = ctx.Indicators.ToDictionary(i => i.Ci.Code, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ImportRow>();
        var pending = new List<(IndicatorInCycle Ci, SaveValueRequest Req, decimal? Value)>();

        for (var r = 0; r < rows.Count; r++)
        {
            var line = r + 2;
            string Cell(string name) => col[name] is var c and >= 0 && c < rows[r].Count ? rows[r][c].Trim() : "";
            var code = Cell("الرمز");
            if (code.Length == 0) continue;
            if (!byCode.TryGetValue(code, out var ci))
            {
                items.Add(new(line, code, null, "error", null, [Err("unknown_code", "رمز غير موجود في هذا النموذج.")]));
                continue;
            }
            if (!seen.Add(code))
            {
                items.Add(new(line, code, ci.Version.NameAr, "error", null, [Err("duplicate_row", "المؤشر مكرر في الملف.")]));
                continue;
            }

            var issues = new List<IssueDto>();
            var kindText = Cell("الحالة");
            var kind = kindText switch
            {
                NotAvailableText => ValueKind.NotAvailable,
                NotApplicableText => ValueKind.NotApplicable,
                "" or "رقم" => ValueKind.Number,
                _ => (ValueKind?)null,
            };
            if (kind is null)
            {
                items.Add(new(line, code, ci.Version.NameAr, "error", kindText, [Err("bad_kind", $"الحالة إما فارغة أو «رقم» أو «{NotAvailableText}» أو «{NotApplicableText}».")]));
                continue;
            }
            var value = Parse(Cell("القيمة"), "القيمة", issues);
            var num = Parse(Cell("البسط"), "البسط", issues);
            var den = Parse(Cell("المقام"), "المقام", issues);
            var reason = Cell("السبب") is { Length: > 0 } rs ? rs : null;
            var explanation = Cell("التفسير") is { Length: > 0 } ex ? ex : null;
            if (kind == ValueKind.Number && value is null && num is null && den is null && issues.Count == 0)
            {
                items.Add(new(line, code, ci.Version.NameAr, "skip", null, []));
                continue;
            }
            if (issues.Count > 0)
            {
                items.Add(new(line, code, ci.Version.NameAr, "error", null, issues));
                continue;
            }

            var spec = CycleSpec.Of(ci.Version);
            var (computed, found) = ValueRules.Check(spec, new ValueInput(kind.Value, value, num, den, reason, explanation),
                ctx.Previous.GetValueOrDefault(ci.Ci.IndicatorId), bigChange);
            issues.AddRange(found.Select(i => new IssueDto(i.Code, i.Severity, i.MessageAr)));
            var display = kind switch
            {
                ValueKind.NotAvailable => NotAvailableText,
                ValueKind.NotApplicable => NotApplicableText,
                _ => computed is { } c ? ValueRules.Fmt(c) + (spec.Unit.Trim() == "%" ? "%" : "") : null,
            };
            ctx.Values.TryGetValue(ci.Ci.IndicatorId, out var current);
            var same = current is not null && current.Kind == kind && current.Value == (kind == ValueKind.Number ? computed : null)
                       && current.Numerator == num && current.Denominator == den && current.Explanation == explanation;
            var status = issues.Any(i => i.Severity == IssueSeverity.Error) ? "error" : same ? "same" : current is null ? "new" : "changed";
            items.Add(new(line, code, ci.Version.NameAr, status, display, issues));
            if (status is "new" or "changed")
                pending.Add((ci, new SaveValueRequest(ci.Ci.IndicatorId, kind.Value, value, num, den, reason, explanation, null), computed));
        }

        var errors = items.Count(i => i.Status == "error");
        var warnings = items.Count(i => i.Status != "error" && i.Issues.Count > 0);
        var preview = new ImportPreview(items.Count, pending.Count, errors, warnings, errors == 0 && pending.Count > 0, items);
        if (!apply) return preview;
        if (errors > 0)
            throw new DomainException("import_errors", $"في الملف {errors} صف به أخطاء؛ لم يطبق شيء. صححها ثم أعد الرفع.", "The file has errors; nothing was applied.", 400);
        if (pending.Count == 0)
            throw new DomainException("import_empty", "لا تغييرات في الملف عن القيم المحفوظة.", "No changes in the file.", 400);

        foreach (var (ci, req, value) in pending)
        {
            var row = Upsert(ctx, ci, CycleSpec.Of(ci.Version), req, value, "dc.value_imported");
            ctx.Values[ci.Ci.IndicatorId] = row;
        }
        await SaveValuesAsync(ctx, ct);
        return preview;
    }

    private static IssueDto Err(string code, string message) => new(code, IssueSeverity.Error, message);

    private static string? Num(decimal? v) => v is { } d ? d.ToString("0.####", CultureInfo.InvariantCulture) : null;

    /// <summary>رقم من خلية Excel: يقبل الأرقام العربية الهندية والفاصلة العشرية العربية وعلامة % الزائدة.</summary>
    internal static decimal? Parse(string text, string label, List<IssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = new string(text.Trim().Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            '٫' => '.',
            '٬' or ',' => '\0',
            _ => c,
        }).Where(c => c != '\0' && c != '%' && !char.IsWhiteSpace(c)).ToArray());
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        issues.Add(Err("not_a_number", $"«{label}» ليس رقمًا: {text}"));
        return null;
    }
}
