using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Tier1.Tools;

/// <summary>
/// Seed extraction from the source workbooks (replaces the old Python import tools):
/// <c>--import-structure &lt;xlsx&gt;</c> writes Seed/university-structure.json and <c>--import-kpi &lt;xlsx&gt;</c> writes Seed/kpi-framework.json,
/// which the server embeds and loads at first start. Both copy the source workbook to <c>reference/</c>.
/// </summary>
public static partial class SeedImporters
{
    private static readonly JsonSerializerOptions Output = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The repository root: the nearest folder at or above the current one that holds Tier1.sln (dotnet run starts in the project folder).</summary>
    public static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private static string SeedFolder(string root) => Path.Combine(root, "src", "Tier1.Infrastructure", "Persistence", "Seed");

    // ---------------------------------------------------------------------------------------------------- cell helpers
    private static object? Raw(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return null;
        if (v.IsNumber) return v.GetNumber();
        if (v.IsText) return v.GetText();
        if (v.IsBoolean) return v.GetBoolean();
        return v.ToString();
    }

    private static string? Str(object? v) => v switch
    {
        null => null,
        string s => s,
        double d => d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "True" : "False",
        _ => v.ToString(),
    };

    private static string Clean(object? v) => v is null ? "" : Whitespace().Replace(Str(v)!, " ").Trim();

    private static string? Text(object? v) => v is null ? null : Whitespace().Replace(Str(v)!, " ").Trim();

    private static decimal? Number(object? v) => v switch
    {
        null => null,
        double d => (decimal)d,
        string s when string.IsNullOrWhiteSpace(s) => null,
        string s => decimal.Parse(s, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static JsonNode? NumberNode(decimal? v) => v is null ? null : JsonValue.Create(v.Value);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("[؀-ۿ]")]
    private static partial Regex Arabic();

    private static IEnumerable<string> Lines(object? v) => (Str(v) ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);

    private static void Write(string path, JsonNode data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, data.ToJsonString(Output) + "\n", new System.Text.UTF8Encoding(false));
    }

    private static void CopySource(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, overwrite: true);
    }

    // ---------------------------------------------------------------------------------------------------- university structure
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["قيادة الجامعة"] = "Sector", ["قطاع"] = "Sector", ["جهاز إداري"] = "Sector", ["إدارة مركزية"] = "Administration", ["إدارة عامة"] = "Administration",
        ["كلية"] = "College", ["معهد"] = "Institute", ["قسم علمي"] = "Department", ["قسم علمي مساند"] = "Department",
    };

    // The central HR administration reports to the university secretary-general sector.
    private static readonly Dictionary<string, string> ParentOfLeadership = new(StringComparer.Ordinal) { ["HR01"] = "L02" };

    // Obvious typos in the source file, and names that changed by decision after it was prepared.
    private static readonly Dictionary<string, string> NameFixes = new(StringComparer.Ordinal)
    {
        ["اإدارة لأمن الجامعي"] = "إدارة الأمن الجامعي",
        ["قسم المكتبات والمعلومات"] = "قسم علم المعلومات",
    };

    private static List<IReadOnlyList<string>> StructureRows(IXLWorksheet ws)
    {
        var rows = new List<IReadOnlyList<string>>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 3; r <= last; r++)
        {
            var cells = Enumerable.Range(1, 8).Select(c => Clean(Raw(ws.Cell(r, c)))).ToList();
            if (cells[0].Length > 0) rows.Add(cells);
        }
        return rows;
    }

    /// <summary>Converts the official university structure workbook to Seed/university-structure.json. Returns a summary line.</summary>
    public static string ImportStructure(string workbookPath, string repoRoot)
    {
        using var wb = new XLWorkbook(workbookPath);
        var units = new List<JsonObject>();
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string code, string name, string kind, string? parent, int order)
        {
            name = NameFixes.GetValueOrDefault(name, name);
            if (!CodePattern().IsMatch(code)) throw new InvalidOperationException($"Invalid code: {code}");
            if (units.Any(u => u["code"]!.GetValue<string>() == code)) throw new InvalidOperationException($"Duplicate code: {code}");
            units.Add(new JsonObject { ["code"] = code, ["nameAr"] = name, ["kind"] = kind, ["parent"] = parent, ["sortOrder"] = order, ["viewsWholeUniversity"] = false });
            byName[name] = code;
        }

        string? university = null;
        var leadership = StructureRows(wb.Worksheet("القطاعات والقيادة"));
        for (var i = 0; i < leadership.Count; i++)
        {
            var (code, name, kind) = (leadership[i][0], leadership[i][1], leadership[i][2]);
            if (kind == "جامعة") { university = name; continue; }
            Add(code, name, Kinds[kind], ParentOfLeadership.GetValueOrDefault(code), i);
        }

        var administrations = StructureRows(wb.Worksheet("الإدارات العامة"));
        for (var i = 0; i < administrations.Count; i++)
        {
            var (code, name, parent) = (administrations[i][0], administrations[i][1], administrations[i][3]);
            if (!byName.TryGetValue(parent, out var parentCode)) throw new InvalidOperationException($"{code}: unknown parent «{parent}»");
            Add(code, name, "Administration", parentCode, i + 1);
        }

        var colleges = StructureRows(wb.Worksheet("الكليات والمعاهد"));
        for (var i = 0; i < colleges.Count; i++) Add(colleges[i][0], colleges[i][1], Kinds[colleges[i][2]], null, 100 + i);

        var collegeCodes = units.Where(u => u["kind"]!.GetValue<string>() is "College" or "Institute").Select(u => u["code"]!.GetValue<string>()).ToHashSet();
        var skipped = new List<string>();
        foreach (var row in StructureRows(wb.Worksheet("الأقسام العلمية")))
        {
            var (code, college, name, kind) = (row[0], row[1], row[3], row[4]);
            if (!Kinds.ContainsKey(kind)) { skipped.Add(code); continue; }
            if (!collegeCodes.Contains(college)) throw new InvalidOperationException($"{code}: unknown college {college}");
            Add(code, name, Kinds[kind], college, int.Parse(code[(code.LastIndexOf('D') + 1)..], CultureInfo.InvariantCulture));
        }

        // Requested by the university but not in the file: both report to the president's sector and see the whole university.
        units.Add(new JsonObject { ["code"] = "SPU", ["nameAr"] = "وحدة التخطيط الاستراتيجي", ["kind"] = "Administration", ["parent"] = "L01", ["viewsWholeUniversity"] = true, ["sortOrder"] = 50 });
        units.Add(new JsonObject { ["code"] = "ICT", ["nameAr"] = "مركز الاتصالات وتكنولوجيا المعلومات", ["kind"] = "Center", ["parent"] = "L01", ["viewsWholeUniversity"] = true, ["sortOrder"] = 51 });

        var data = new JsonObject
        {
            ["source"] = Path.GetFileName(workbookPath), ["university"] = university ?? "جامعة العاصمة", ["units"] = new JsonArray(units.Select(u => (JsonNode)u).ToArray()),
        };
        Write(Path.Combine(SeedFolder(repoRoot), "university-structure.json"), data);
        CopySource(workbookPath, Path.Combine(repoRoot, "reference", "university-structure.xlsx"));
        var count = string.Join(", ", units.GroupBy(u => u["kind"]!.GetValue<string>()).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}"));
        return $"Wrote {units.Count} units to Seed/university-structure.json: {count}; skipped (no departments): {(skipped.Count == 0 ? "-" : string.Join(", ", skipped))}";
    }

    // ---------------------------------------------------------------------------------------------------- KPI framework
    private static readonly int[] Years = [2027, 2028, 2029, 2030, 2031];
    private static readonly Dictionary<string, string> PillarCode = new(StringComparer.Ordinal) { ["أ"] = "أ", ["ب"] = "ب", ["ج"] = "ج", ["د"] = "د", ["ه"] = "هـ", ["هـ"] = "هـ", ["و"] = "و" };

    // Collection level, aggregation rule, numerator and denominator per indicator (the workbook does not carry them).
    private static readonly Dictionary<string, (string Level, string Aggregation, string? Numerator, string? Denominator)> Collect = new(StringComparer.Ordinal)
    {
        ["1.01"] = ("Department", "RatioOfSums", "الطلاب الناجحون في المقررات المسجلة (بالحد الأدنى للدرجات)", "إجمالي الطلاب المسجلين في المقررات"),
        ["1.02"] = ("College", "RatioOfSums", "الناجحون والخريجون من السنة النهائية في الوقت المحدد", "طلاب السنة النهائية المتقدمون في الوقت المحدد للدفعة"),
        ["1.03"] = ("College", "RatioOfSums", "الطلاب المشاركون في الأنشطة واللجان والحاصلون على جوائز", "إجمالي الطلاب الملتحقين"),
        ["1.04"] = ("College", "RatioOfSums", "طلاب السنة النهائية الذين أتموا التدريب الميداني اللائحي بنجاح", "طلاب السنة النهائية المستهدفون بالتدريب وفق اللائحة"),
        ["1.05"] = ("College", "RatioOfSums", "الطلاب الذين عُيّن لهم مرشد أكاديمي فعال ويتابعون بانتظام", "إجمالي الطلاب الملتحقين"),
        ["1.06"] = ("College", "Sum", null, null),
        ["1.07"] = ("College", "RatioOfSums", "الطلاب المسجلون في التخصصات ذات الأولوية", "إجمالي الطلاب الملتحقين"),
        ["1.08"] = ("College", "WeightedAverage", null, "عدد المستجيبين للاستبيان"),
        ["2.01"] = ("University", "RatioOfSums", "الأنشطة التسويقية المكتملة", "الأنشطة التسويقية المخططة"),
        ["2.02"] = ("College", "RatioOfSums", "المسجلون هذا العام − المسجلون العام الماضي", "المسجلون العام الماضي"),
        ["2.03"] = ("College", "RatioOfSums", "الخريجون الموظفون فعليًا", "الخريجون المستجيبون للاستبيان"),
        ["2.04"] = ("College", "WeightedAverage", null, "عدد المستجيبين للاستبيان"),
        ["3.01"] = ("College", "Sum", null, null),
        ["3.02"] = ("College", "RatioOfSums", "البرامج الأكاديمية المرتبطة والمعتمدة لسوق العمل", "إجمالي البرامج الأكاديمية"),
        ["4.01"] = ("College", "Sum", null, null),
        ["4.02"] = ("Department", "DistinctCount", null, null),
        ["4.03"] = ("College", "Sum", null, null),
        ["4.04"] = ("College", "Sum", null, null),
        ["4.05"] = ("College", "RatioOfSums", "المشروعات والأبحاث الممولة من جهات خارجية", "إجمالي المشروعات والأبحاث المسجلة"),
        ["4.06"] = ("College", "RatioOfSums", "برامج الدراسات العليا الدولية المشتركة", "إجمالي برامج الدراسات العليا القائمة"),
        ["4.07"] = ("College", "RatioOfSums", "الكوادر الأكاديمية الفعالة في مشروعات بحثية مشتركة", "إجمالي الكوادر الأكاديمية والبحثية"),
        ["4.08"] = ("College", "RatioOfSums", "الأبحاث المنشورة في مجلات Q1", "إجمالي الأبحاث المنشورة"),
        ["4.09"] = ("College", "Sum", null, null),
        ["4.10"] = ("College", "DistinctCount", null, null),
        ["4.11"] = ("College", "DistinctCount", null, null),
        ["5.01"] = ("University", "RatioOfSums", "أنشطة ومبادرات خطة التدويل المنفذة", "أنشطة خطة التدويل المخططة"),
        ["5.02"] = ("College", "DistinctCount", null, null),
        ["5.03"] = ("College", "DistinctCount", null, null),
        ["5.04"] = ("College", "Sum", null, null),
        ["5.05"] = ("College", "DistinctCount", null, null),
        ["5.06"] = ("College", "Sum", null, null),
        ["5.07"] = ("College", "WeightedAverage", null, "عدد المستجيبين للاستبيان"),
        ["6.01"] = ("College", "RatioOfSums", "الطلاب الوافدون المقيدون", "إجمالي الطلاب الملتحقين"),
        ["6.02"] = ("College", "RatioOfSums", "الطلاب الوافدون والمغادرون في برامج التبادل", "إجمالي الطلاب الملتحقين"),
        ["6.03"] = ("College", "DistinctCount", null, null),
        ["6.04"] = ("College", "RatioOfSums", "الوافدون هذا العام − الوافدون العام السابق", "الوافدون العام السابق"),
        ["7.01"] = ("College", "RatioOfSums", "أعضاء هيئة التدريس الذين استكملوا تقييم الأداء المعتمد", "أعضاء هيئة التدريس المستهدفون بالتقييم"),
        ["7.02"] = ("College", "RatioOfSums", "الطلاب المنتظمون بدوام كامل", "أعضاء هيئة التدريس بدوام كامل"),
        ["7.03"] = ("College", "RatioOfSums", "مجموع ساعات الاتصال الأسبوعية المجدولة", "أعضاء هيئة التدريس بوقت كامل"),
        ["7.04"] = ("College", "RatioOfSums", "أعضاء هيئة التدريس ذوو الحضور الموثق في مؤتمرات دولية", "إجمالي أعضاء هيئة التدريس"),
        ["7.05"] = ("College", "RatioOfSums", "إجمالي ساعات التدريب الموثقة لأعضاء هيئة التدريس", "أعضاء هيئة التدريس بوقت كامل"),
        ["7.06"] = ("College", "RatioOfSums", "البرامج التدريبية المطورة مشتركة مع الصناعة", "إجمالي البرامج التدريبية المقدمة"),
        ["7.07"] = ("College", "RatioOfSums", "أعضاء هيئة التدريس الذين طوروا مواد تعليمية تفاعلية موثقة", "إجمالي أعضاء هيئة التدريس"),
        ["7.08"] = ("College", "RatioOfSums", "الأبحاث المنشورة والموثقة دوليًا", "أعضاء هيئة التدريس على رأس العمل"),
        ["7.09"] = ("College", "RatioOfSums", "المستجيبون بـ«راضٍ» أو «راضٍ جدًا»", "المشاركون في استبيان أعضاء هيئة التدريس"),
        ["8.01"] = ("University", "RatioOfSums", "مؤشرات الأداء التي حققت المستهدف أو تجاوزته", "إجمالي مؤشرات الأداء المؤسسية"),
        ["8.02"] = ("University", "RatioOfSums", "طلبات الخدمة المنجزة في الإطار الزمني المعتمد", "إجمالي طلبات الخدمة المستلمة"),
        ["8.03"] = ("University", "RatioOfSums", "الكليات الحاصلة على الاعتماد المؤسسي الفعال", "إجمالي كليات الجامعة"),
        ["8.04"] = ("College", "RatioOfSums", "البرامج الأكاديمية المعتمدة", "إجمالي البرامج الأكاديمية المطروحة"),
        ["8.05"] = ("University", "RatioOfSums", "العمليات التشغيلية الموثقة والمعتمدة", "إجمالي العمليات المستهدفة"),
        ["8.06"] = ("University", "RatioOfSums", "ملاحظات التدقيق المغلقة في المدة المحددة", "إجمالي ملاحظات التدقيق الصادرة"),
        ["8.07"] = ("University", "RatioOfSums", "إجراءات ADRI المكتملة", "إجراءات ADRI المخططة"),
        ["8.08"] = ("College", "RatioOfSums", "الأقسام المستكملة للمراجعة الدورية في الموعد", "الأقسام المستحقة للمراجعة"),
        ["8.09"] = ("College", "RatioOfSums", "الطلاب الذين تلقوا التوجيه وإحاطة السياسات الموثقة", "إجمالي الطلاب الملتحقين"),
        ["8.10"] = ("University", "Central", null, null),
        ["9.01"] = ("University", "Central", null, null),
        ["9.02"] = ("College", "Sum", null, null),
        ["9.03"] = ("University", "Central", null, null),
        ["9.04"] = ("College", "RatioOfSums", "البرامج التي تتضمن تدريبًا ميدانيًا معتمدًا", "إجمالي البرامج الأكاديمية الفعالة"),
        ["9.05"] = ("College", "RatioOfSums", "المقررات الإلكترونية أو المدمجة", "إجمالي المقررات المطروحة"),
        ["9.06"] = ("College", "RatioOfSums", "مخرجات التعلم التي حققت المستوى المستهدف", "مخرجات التعلم التي قُيّمت"),
        ["9.07"] = ("College", "RatioOfSums", "ملاحظات الطلاب التي نُفذت إجراءات بشأنها", "إجمالي ملاحظات الطلاب المستلمة"),
        ["9.08"] = ("College", "RatioOfSums", "أعضاء هيئة التدريس الذين أتموا مراجعة تدريس رسمية", "إجمالي أعضاء هيئة التدريس"),
        ["9.09"] = ("College", "RatioOfSums", "توصيات الفاحصين الخارجيين المطبقة", "إجمالي توصيات الفاحصين المستلمة"),
        ["9.10"] = ("College", "WeightedAverage", null, "عدد البرامج التي رُوجعت"),
        ["9.11"] = ("College", "RatioOfSums", "المقررات التي نُشرت معايير تقييمها قبل بدء الدراسة", "إجمالي المقررات المطروحة"),
        ["9.12"] = ("College", "RatioOfSums", "دورات التقييم المتحقق منها", "إجمالي دورات التقييم"),
        ["9.13"] = ("College", "RatioOfSums", "المقررات التي استكملت إجراءات الاعتدال والمراجعة", "إجمالي المقررات المستهدفة"),
        ["10.01"] = ("University", "RatioOfSums", "قيمة المنح الدراسية والمساعدات المالية الممنوحة", "إجمالي إيرادات الرسوم الدراسية"),
        ["10.02"] = ("University", "RatioOfSums", "الإيرادات التشغيلية − المصروفات التشغيلية", "إجمالي الإيرادات التشغيلية"),
        ["10.03"] = ("College", "Sum", null, null),
        ["10.04"] = ("University", "RatioOfSums", "المصروفات الإدارية والداعمة", "إجمالي المصروفات التشغيلية"),
        ["10.05"] = ("University", "RatioOfSums", "الإيرادات غير الناتجة عن الرسوم الدراسية", "إجمالي الإيرادات"),
        ["10.06"] = ("College", "Sum", null, null),
        ["10.07"] = ("College", "Sum", null, null),
        ["10.08"] = ("College", "RatioOfSums", "المعامل الحاصلة على اعتماد دولي ساري", "إجمالي عدد المعامل"),
        ["11.01"] = ("University", "RatioOfSums", "المساحات التعليمية المستخدمة", "المساحات التعليمية المتاحة"),
        ["11.02"] = ("University", "Central", null, null),
        ["11.03"] = ("University", "Central", null, null),
        ["11.04"] = ("University", "RatioOfSums", "المباني الجيدة السليمة", "إجمالي المباني الرئيسية"),
        ["11.05"] = ("University", "RatioOfSums", "تكلفة الصيانة المؤجلة", "إجمالي قيمة الاستبدال"),
        ["11.06"] = ("University", "Central", null, null),
        ["11.07"] = ("University", "RatioOfSums", "الطلاب المسكنون فعليًا في المدن الجامعية", "الطاقة الاستيعابية المتاحة (الأسرة)"),
        ["11.08"] = ("University", "RatioOfSums", "الطلاب المستجيبون بـ«راضٍ» أو «راضٍ جدًا»", "الطلاب المشاركون في استبيان الخدمات"),
        ["12.01"] = ("University", "Central", null, null),
        ["12.02"] = ("University", "Central", null, null),
        ["12.03"] = ("University", "RatioOfSums", "المعاملات الرقمية", "إجمالي المعاملات"),
        ["12.04"] = ("University", "RatioOfSums", "الإنفاق على تقنية المعلومات", "إجمالي الميزانية التشغيلية"),
        ["12.05"] = ("University", "RatioOfSums", "الكوادر المستكملون للتدريب على تقنية المعلومات", "إجمالي الكوادر المطلوب تدريبهم"),
        ["12.06"] = ("University", "RatioOfSums", "الطلاب ذوو وصول فعال إلى الحوسبة الافتراضية/السحابة", "إجمالي الطلاب الملتحقين"),
        ["12.07"] = ("University", "RatioOfSums", "الخدمات الجامعية المؤتمتة والمتاحة إلكترونيًا", "إجمالي الخدمات المقدمة من الجامعة"),
        ["12.08"] = ("College", "RatioOfSums", "الاختبارات المنفذة والمصححة إلكترونيًا بالكامل", "إجمالي الاختبارات المنفذة"),
    };

    // Approved corrections to the workbook's data: (field, new value, explanation). They are recorded in the indicator's plan note.
    private static readonly Dictionary<string, (string Field, string Value, string Why)[]> Corrections = new(StringComparer.Ordinal)
    {
        ["7.03"] = [("direction", "LowerIsBetter", "صحح النمط إلى تنازلي (الأقل أفضل): مستهدفات الملف تنخفض من 13 إلى 8 ساعات، بينما سجل النمط تصاعديًا فكان الإنجاز سيحسب خطأ.")],
        ["8.08"] = [("target", "2029:78", "صحح مستهدف السنة 3 (2029) من 70 إلى 78: مستهدفات الملف 73 ثم 76 ثم 70 ثم 80، فكان يهبط في السنة الثالثة.")],
        ["11.02"] = [("unit", "كيلوواط ساعة (إجمالي سنوي)", "صححت الوحدة من «كيلوواط ساعة / م²» إلى إجمالي الكيلوواط ساعة: قيم الملف (نحو 21 مليونًا) استهلاك كلي لا معدل للمتر المربع.")],
        ["7.05"] = [("unit", "ساعة / عضو / سنة", "صححت الوحدة من «% ساعة/عضو/سنة» إلى «ساعة / عضو / سنة»: المؤشر ساعات لا نسبة.")],
        ["7.08"] = [("unit", "بحث / عضو", "صححت الوحدة من «% (بحث/عضو)» إلى «بحث / عضو»: المؤشر متوسط أبحاث لا نسبة.")],
    };

    // Notes that explain a special target without changing its value.
    private static readonly Dictionary<string, string> Notes = new(StringComparer.Ordinal)
    {
        ["2.02"] = "المستهدف صفر في كل السنوات يعني ألا يتراجع التسجيل: يحسب محققًا إذا كان النمو صفرًا أو أكثر، وغير محقق إذا تراجع.",
        ["9.01"] = "قيمة الحالة: 1 = معتمد، 0 = غير معتمد. المطلوب الاعتماد في السنة الخامسة (مستهدفات السنوات قبلها صفر).",
    };

    /// <summary>Name in Arabic and English from a two-line cell (or separated by " / "), in either order.</summary>
    private static (string Ar, string? En) SplitName(object? v)
    {
        var parts = Lines(v).ToList();
        if (parts.Count == 1 && parts[0].Contains(" / ", StringComparison.Ordinal))
            parts = parts[0].Split(" / ").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var ar = string.Join(' ', parts.Where(p => Arabic().IsMatch(p)));
        var en = string.Join(' ', parts.Where(p => !Arabic().IsMatch(p)));
        return (ar, en.Length == 0 ? null : en);
    }

    private static int? FirstNumber(object? v)
    {
        var m = Regex.Match(Str(v) ?? "", @"\d+");
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    private static List<string> SplitActivities(object? v)
    {
        var result = new List<string>();
        foreach (var item in Regex.Split(Str(v) ?? "", "[؛;،,]"))
        {
            var s = Whitespace().Replace(item, " ").Trim().Trim(' ', '.', '-');
            if (s.Length >= 3 && !result.Contains(s)) result.Add(s);
        }
        return result;
    }

    /// <summary>Converts the comprehensive university performance framework workbook (KPI.xlsx) to Seed/kpi-framework.json. Returns a summary line.</summary>
    public static string ImportKpi(string workbookPath, string repoRoot)
    {
        using var wb = new XLWorkbook(workbookPath);
        var frame = wb.Worksheets.First(w => w.Name.Contains("الإطار العام للتقييم", StringComparison.Ordinal));
        var card = wb.Worksheets.First(w => w.Name.Contains("بطاقة مؤشرات الأداء", StringComparison.Ordinal));
        var links = wb.Worksheets.First(w => w.Name.Contains("تكامل مؤشرات الأداء", StringComparison.Ordinal));

        // Pillars and standards from the general framework sheet.
        var pillars = new JsonArray();
        var standards = new JsonArray();
        string? current = null;
        for (var r = 7; r <= 18; r++)
        {
            var letter = Text(Raw(frame.Cell(r, 1)));
            if (!string.IsNullOrEmpty(letter))
            {
                var (ar, en) = SplitName(Raw(frame.Cell(r, 2)));
                current = PillarCode[letter];
                pillars.Add(new JsonObject { ["code"] = current, ["nameAr"] = ar, ["nameEn"] = en, ["sortOrder"] = pillars.Count + 1 });
            }
            var raw = Raw(frame.Cell(r, 3));
            if (raw is null || Str(raw) == "") continue;
            var lines = Lines(raw).ToList();
            var m = Regex.Match(lines[0], @"المعيار\s*(\d+)\s*:\s*(.+)");
            var number = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var rest = string.Join(' ', lines.Skip(1));
            standards.Add(new JsonObject
            {
                ["code"] = m.Groups[1].Value, ["nameAr"] = m.Groups[2].Value.Trim(), ["nameEn"] = rest.Length == 0 ? null : rest,
                ["description"] = Text(Raw(frame.Cell(r, 4))), ["pillar"] = current, ["sortOrder"] = number,
            });
        }

        // Link rationale from the hidden sheet.
        var rationale = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        var linkLast = links.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 2; r <= linkLast; r++)
        {
            var code = Raw(links.Cell(r, 3));
            if (code is null) continue;
            var key = (code is double d ? d : double.Parse(Str(code)!, CultureInfo.InvariantCulture)).ToString("F2", CultureInfo.InvariantCulture);
            rationale[key] = (Text(Raw(links.Cell(r, 5))), Text(Raw(links.Cell(r, 6))));
        }

        // Indicators from the performance card sheet.
        var indicators = new JsonArray();
        var initiatives = new List<(string Standard, string Name, List<string> Codes)>();
        var cardLast = card.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 10; r <= cardLast; r++)
        {
            var code = Text(Raw(card.Cell(r, 3)));
            var nameCell = Raw(card.Cell(r, 14));
            if (string.IsNullOrEmpty(code) || nameCell is null || Str(nameCell) == "") continue;
            var standard = Str(Raw(card.Cell(r, 2))) ?? "None";
            var (ar, en) = SplitName(nameCell);
            var kind = Str(Raw(card.Cell(r, 80))) ?? "";
            var pattern = Str(Raw(card.Cell(r, 81))) ?? "";
            var yearly = Enumerable.Range(82, 4).Select(c => Number(Raw(card.Cell(r, c)))).ToList();
            var final = Number(Raw(card.Cell(r, 20)));
            var (level, aggregation, numerator, denominator) = Collect.TryGetValue(code, out var c0) ? c0 : throw new InvalidOperationException($"Indicator {code} has no collection rule.");
            var item = new JsonObject
            {
                ["code"] = code, ["row"] = r, ["standard"] = standard, ["sortOrder"] = indicators.Count + 1, ["nameAr"] = ar, ["nameEn"] = en,
                ["definition"] = Text(Raw(card.Cell(r, 15))), ["unit"] = Text(Raw(card.Cell(r, 16))) is { Length: > 0 } u ? u : "%",
                ["dataSource"] = Text(Raw(card.Cell(r, 17))), ["formula"] = Text(Raw(card.Cell(r, 18))),
                ["direction"] = pattern.Contains("تنازل", StringComparison.Ordinal) ? "LowerIsBetter" : "HigherIsBetter",
                ["accumulation"] = kind.Contains("تراكمي", StringComparison.Ordinal) && !kind.Contains("غير", StringComparison.Ordinal) ? "Cumulative" : "Snapshot",
                ["level"] = level, ["aggregation"] = aggregation, ["numerator"] = numerator, ["denominator"] = denominator,
                ["perspectives"] = new JsonArray(Enumerable.Range(0, 7).Where(i => (Str(Raw(card.Cell(r, 7 + i))) ?? "").StartsWith('✔')).Select(i => (JsonNode)(i + 1)).ToArray()),
                ["sdgPrimary"] = FirstNumber(Raw(card.Cell(r, 4))), ["sdgSecondary"] = FirstNumber(Raw(card.Cell(r, 5))), ["sdgInduced"] = FirstNumber(Raw(card.Cell(r, 6))),
                ["pssRationale"] = rationale.GetValueOrDefault(code).Item1, ["sdgRationale"] = rationale.GetValueOrDefault(code).Item2,
            };
            var targets = Years.Zip(yearly.Append(final)).Where(p => p.Second is not null).Select(p => (Year: p.First, Value: p.Second!.Value)).ToList();
            var planNote = Notes.GetValueOrDefault(code);
            var why = new JsonArray();
            foreach (var (field, value, text) in Corrections.GetValueOrDefault(code) ?? [])
            {
                if (field == "target")
                {
                    var parts = value.Split(':');
                    for (var i = 0; i < targets.Count; i++)
                        if (targets[i].Year == int.Parse(parts[0], CultureInfo.InvariantCulture)) targets[i] = (targets[i].Year, decimal.Parse(parts[1], CultureInfo.InvariantCulture));
                }
                else item[field] = value;
                why.Add(text);
            }
            item["targets"] = new JsonArray(targets.Select(t => (JsonNode)new JsonObject { ["year"] = t.Year, ["value"] = NumberNode(t.Value) }).ToArray());
            if (why.Count > 0) planNote = string.Join(' ', new[] { planNote }.Concat(why.Select(w => w!.GetValue<string>())).Where(s => !string.IsNullOrEmpty(s)));
            item["planNote"] = planNote;
            item["corrections"] = why;
            indicators.Add(item);

            foreach (var activity in SplitActivities(Raw(card.Cell(r, 19))))
            {
                var found = initiatives.FindIndex(x => x.Standard == standard && x.Name == activity);
                if (found < 0) initiatives.Add((standard, activity, [code]));
                else initiatives[found].Codes.Add(code);
            }
        }

        var seed = new JsonObject
        {
            ["source"] = Path.GetFileName(workbookPath), ["years"] = new JsonArray(Years.Select(y => (JsonNode)y).ToArray()), ["pillars"] = pillars, ["standards"] = standards, ["indicators"] = indicators,
            ["initiatives"] = new JsonArray(initiatives.Select(i => (JsonNode)new JsonObject
            {
                ["standard"] = i.Standard, ["nameAr"] = i.Name, ["indicatorCodes"] = new JsonArray(i.Codes.Select(c => (JsonNode)c).ToArray()),
            }).ToArray()),
        };
        Write(Path.Combine(SeedFolder(repoRoot), "kpi-framework.json"), seed);
        CopySource(workbookPath, Path.Combine(repoRoot, "reference", "KPI.xlsx"));
        var corrected = indicators.Count(i => i!["corrections"]!.AsArray().Count > 0);
        return $"pillars={pillars.Count} standards={standards.Count} indicators={indicators.Count} initiatives={initiatives.Count} corrected={corrected}";
    }
}
