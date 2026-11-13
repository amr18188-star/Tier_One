#pragma warning disable CA5394 // Generated demo data: the randomness here is not security related.
using System.Text.Json.Nodes;

namespace Tier1.Api.Startup.DemoData;

internal sealed record College(string Code, string Entry, string Committee, string Dean, double Level, int Size, (string Code, string Rep)[] Departments);

internal sealed record CycleSpec(string Name, int Quarter, string Opens, string Due, double Trend, bool Closed);

/// <summary>One run of the demo data: college structures first, then the quarterly collection cycles.</summary>
internal sealed class DemoRun(Uri baseAddress, string password, ILogger log) : IDisposable
{
    private const int TargetYear = 2027; // 2026 is the baseline year; targets and quarterly cycles start in 2027.
    private static readonly string[] IntegerUnits = ["عدد", "برنامج", "دولة", "سرير", "مركز"];

    private static readonly College[] Colleges =
    [
        new("ENG", "entry", "college", "dean", 1.0, 9000, [("ENG-CIV", "deptrep"), ("ENG-ELE", "rep.ele"), ("ENG-MEC", "rep.mec")]),
        new("CS", "entry.cs", "college.cs", "dean.cs", 1.07, 4500, [("CS-SE", "rep.se"), ("CS-AI", "rep.ai")]),
        new("COM", "entry.com", "college.com", "dean.com", 0.9, 16000, [("COM-ACC", "rep.acc"), ("COM-BUS", "rep.bus")]),
    ];

    private static readonly Dictionary<string, double> Share = new() { ["ENG"] = 0.35, ["CS"] = 0.2, ["COM"] = 0.45 };

    private static readonly CycleSpec[] Cycles =
    [
        new("الربع الأول 2027 (تجريبي)", 1, "2027-04-01", "2027-04-30", 0.92, true),
        new("الربع الثاني 2027 (تجريبي)", 2, "2027-07-01", "2027-07-31", 0.96, true),
        new("الربع الثالث 2027 (تجريبي)", 3, "2027-10-01", "2027-10-31", 1.0, false),
    ];

    // Current quarter: where each unit's form stands, so every account finds something waiting in "required from you".
    private static readonly Dictionary<string, string> CurrentStages = new()
    {
        ["ENG"] = "Dean", ["ENG-CIV"] = "DepartmentReview", ["ENG-ELE"] = "CollegeCommittee", ["ENG-MEC"] = "partial",
        ["CS"] = "Done", ["CS-SE"] = "Done", ["CS-AI"] = "Quality",
        ["COM"] = "returned", ["COM-ACC"] = "Dean", ["COM-BUS"] = "empty",
        ["QAC"] = "Done", ["STU"] = "Quality", ["DIG"] = "partial", ["INTL"] = "Done", ["CU"] = "partial",
    };

    // On the official university structure the demo colleges and central units are the real units with these codes.
    private static readonly Dictionary<string, string> OfficialToDemo = new()
    {
        ["F06"] = "ENG", ["F06-D01"] = "ENG-CIV", ["F06-D02"] = "ENG-ELE", ["F06-D03"] = "ENG-MEC",
        ["F08"] = "CS", ["F08-D01"] = "CS-SE", ["F08-D04"] = "CS-AI",
        ["F17"] = "COM", ["F17-D01"] = "COM-ACC", ["F17-D02"] = "COM-BUS",
        ["SPU"] = "QAC", ["A06"] = "STU", ["ICT"] = "DIG", ["A11"] = "INTL",
    };

    private static readonly HashSet<string> CentralUnits = ["CU", "QAC", "STU", "DIG", "INTL"];

    private static readonly string[] First = ["أحمد", "محمد", "محمود", "مصطفى", "خالد", "هشام", "طارق", "سامح", "وليد", "عمرو", "منى", "هبة", "نهى", "ريهام", "داليا", "سارة", "إيمان", "ياسمين"];
    private static readonly string[] Family = ["عبد الحميد", "السيد", "منصور", "الشافعي", "عبد الله", "حسنين", "رضوان", "سليمان", "فهمي", "الجمال", "عثمان", "البنا", "زكي", "شاكر"];

    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    private readonly Dictionary<string, DemoClient> clients = [];
    private Dictionary<string, JsonNode> units = [];
    private Dictionary<string, JsonNode> unitsById = [];
    private CancellationToken ct;

    public void Dispose()
    {
        foreach (var c in clients.Values) c.Dispose();
    }

    private async Task<DemoClient> UserAsync(string name)
    {
        if (!clients.TryGetValue(name, out var client))
            clients[name] = client = await DemoClient.LoginAsync(baseAddress, $"{name}@tier1.local", password, ct);
        return client;
    }

    public async Task ExecuteAsync(CancellationToken token)
    {
        ct = token;
        var planner = await UserAsync("planner");
        var existing = (await planner.GetAsync("/api/cycles", ct)).Items().Select(c => c["name"].Str()).ToHashSet();
        if (Cycles.Any(c => existing.Contains(c.Name)))
        {
            log.LogInformation("Demo data is already loaded; skipping.");
            return;
        }
        var started = DateTime.UtcNow;
        log.LogInformation("Loading the demo data through {Address}.", baseAddress);

        var list = (await planner.GetAsync("/api/org/units", ct)).Items().ToList();
        // Work with the demo codes whichever structure is loaded: the official codes of the demo colleges are renamed in memory only.
        if (list.Any(u => u["code"].Str() == "F06"))
            foreach (var u in list.Where(u => OfficialToDemo.ContainsKey(u["code"].Str()!))) u["code"] = OfficialToDemo[u["code"].Str()!];
        units = list.ToDictionary(u => u["code"].Str()!);
        unitsById = units.Values.ToDictionary(u => u["id"].Str()!);
        var missing = new[] { "CU", "QAC", "STU", "DIG", "INTL", "ENG", "CS", "COM" }.Where(c => !units.ContainsKey(c)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Missing demo units {string.Join(',', missing)}; Seed:Demo must create the demo colleges or Seed:Structure the official ones.");

        await StructuresAsync();
        foreach (var spec in Cycles) await RunCycleAsync(spec);
        log.LogInformation("Demo data loaded in {Seconds:0} seconds.", (DateTime.UtcNow - started).TotalSeconds);
    }

    // ---------------------------------------------------------------------------------------------------- structures
    private async Task StructuresAsync()
    {
        var template = (await (await UserAsync("college")).GetAsync("/api/structure/template", ct)).Items().ToList();
        var added = 0;
        foreach (var college in Colleges)
        {
            var client = await UserAsync(college.Committee);
            var collegeUnit = units[college.Code];
            var r = new Rnd("structure", college.Code);
            foreach (var tu in template)
            {
                List<JsonNode?> departments = tu["perDepartment"].Truthy() ? college.Departments.Select(d => (JsonNode?)units[d.Code]).ToList() : [null];
                foreach (var dept in departments)
                foreach (var pos in tu["positions"].Items())
                {
                    if (college.Code == "COM" && !pos["required"].Truthy()) continue; // vacancies show on the completion board
                    for (var n = 0; n < (pos["single"].Truthy() ? 1 : 2); n++)
                    {
                        var name = $"{(tu["isHead"].Truthy() ? "أ.د." : "د.")} {r.Choice(First)} {r.Choice(Family)}";
                        var body = new JsonObject
                        {
                            ["collegeId"] = collegeUnit["id"].Copy(), ["departmentId"] = dept?["id"].Copy(), ["positionId"] = pos["id"].Copy(), ["fullName"] = name,
                            ["title"] = null, ["employer"] = null, ["email"] = $"member{r.Int(100, 999)}@demo.capu.edu.eg",
                            ["phone"] = $"010{r.Int(10000000, 99999999)}", ["startDate"] = "2025-09-01", ["decisionRef"] = $"قرار مجلس الكلية {r.Int(10, 99)}/2025",
                            ["notes"] = null, ["replaceCurrent"] = false, ["version"] = null,
                        };
                        var (status, err) = await client.CallAsync(HttpMethod.Post, "/api/structure/members", body, ct: ct);
                        if (status == 200) added++;
                        else if (status is not (409 or 400)) throw new InvalidOperationException($"structure {college.Code}: {status} {err}");
                    }
                }
            }
        }
        log.LogInformation("  Added {Count} members to the college structures.", added);
    }

    // ---------------------------------------------------------------------------------------------------- cycles
    private static College? CollegeOf(JsonNode unit) =>
        Colleges.FirstOrDefault(c => c.Code == unit["code"].Str() || c.Departments.Any(d => d.Code == unit["code"].Str()));

    private static int SizeOf(JsonNode unit)
    {
        var c = CollegeOf(unit);
        if (c is null) return 1200;
        return unit["code"].Str() == c.Code ? c.Size : c.Size / c.Departments.Length;
    }

    private static bool IsPercent(JsonNode item) => item["unit"].Str()?.Trim() == "%";

    private static bool IsInteger(JsonNode item) => IntegerUnits.Contains(item["unit"].Str()?.Trim());

    private static double Anchor(JsonNode item) => item["target"].Num() is { } t ? (double)t : IsPercent(item) ? 65.0 : 20.0;

    /// <summary>A realistic value for an indicator in a unit and cycle: near the target, at a level that differs between colleges so comparisons and trends show.</summary>
    private static JsonObject ValueBody(JsonNode item, JsonNode unit, CycleSpec cycle)
    {
        var r = new Rnd(item["code"].Str()!, unit["code"].Str()!);
        var rc = new Rnd(item["code"].Str()!, unit["code"].Str()!, cycle.Quarter);
        var college = CollegeOf(unit);
        var level = college?.Level ?? 0.97;
        var f = level * r.Uniform(0.82, 1.16) * cycle.Trend * rc.Uniform(0.98, 1.02);
        var anchor = Anchor(item);
        var share = college is null ? 1.0 : Share[college.Code] / (unit["code"].Str() == college.Code ? 1 : college.Departments.Length);
        // A sum or distinct count with a university-wide target: the college's target is its share of it.
        if (college is not null && item["target"].Num() is not null && item["aggregation"].Str() is "Sum" or "DistinctCount") anchor *= share;
        var v = item["direction"].Str() == "LowerIsBetter" ? anchor / f : anchor * f;
        var pct = IsPercent(item);
        var body = new JsonObject
        {
            ["indicatorId"] = item["indicatorId"].Copy(), ["kind"] = "Number", ["value"] = null, ["numerator"] = null, ["denominator"] = null,
            ["reason"] = null, ["explanation"] = null, ["version"] = item["value"].Truthy() ? item["value"]!["version"].Copy() : null,
        };
        if (rc.Next() < 0.03)
        {
            body["kind"] = "NotAvailable";
            body["reason"] = "لم تصدر البيانات بعد من الجهة المصدرة";
        }
        else if (item["usesRatio"].Truthy())
        {
            if (pct)
            {
                v = Math.Max(1.0, Math.Min(v, 99.0));
                var d = Math.Max(20, (int)(SizeOf(unit) * r.Uniform(0.3, 1.0)));
                body["numerator"] = Math.Min(d, (int)Math.Round(v / 100 * d));
                body["denominator"] = d;
            }
            else
            {
                var d = r.Int(150, 900);
                body["numerator"] = Math.Max(0, (int)Math.Round(v * d));
                body["denominator"] = d;
            }
        }
        else if (item["aggregation"].Str() == "WeightedAverage")
        {
            body["value"] = Math.Round(pct ? Math.Min(v, 100.0) : v, 2);
            body["denominator"] = Math.Max(10, (int)(SizeOf(unit) * r.Uniform(0.2, 1.0)));
        }
        else if (IsInteger(item)) body["value"] = Math.Max(0, (int)Math.Round(v));
        else body["value"] = Math.Round(pct ? Math.Min(v, 100.0) : v, 2);
        return body;
    }

    private static List<JsonNode> ItemsOf(JsonNode form) => form["sections"].Items().SelectMany(s => s["items"].Items()).ToList();

    private async Task FillAsync(DemoClient client, JsonNode form, JsonNode unit, CycleSpec cycle, int? limit = null)
    {
        var formId = form["id"].Str();
        foreach (var item in ItemsOf(form).Take(limit ?? int.MaxValue))
        {
            var body = ValueBody(item, unit, cycle);
            var (status, saved) = await client.CallAsync(HttpMethod.Put, $"/api/forms/{formId}/values", body, ct: ct);
            if (status != 200) throw new InvalidOperationException($"{unit["code"].Str()} {item["code"].Str()}: {status} {saved}");
            if (saved!["issues"].Items().Any(i => i["code"].Str() == "big_change"))
            {
                body["explanation"] = "تغير متوقع بعد تحديث نظام القبول والتسجيل";
                body["version"] = saved["value"]!["version"].Copy();
                await client.CallAsync(HttpMethod.Put, $"/api/forms/{formId}/values", body, 200, ct);
            }
            if (item["evidenceRequired"].Truthy() && body["kind"].Str() == "Number" && !saved["evidence"].Items().Any(e => !e["voided"].Truthy()))
                await client.UploadAsync($"/api/forms/{formId}/values/{item["indicatorId"].Str()}/evidence", $"{item["code"].Str()}.pdf", Pdf,
                    new Dictionary<string, string> { ["description"] = "كشف معتمد من الجهة (ملف تجريبي)" }, ct);
        }
    }

    /// <summary>Who reviews each step of a unit's form (separation of duties: never the one who entered it).</summary>
    private static Dictionary<string, string> Reviewers(JsonNode unit)
    {
        var c = CollegeOf(unit);
        if (c is null) return new() { ["Quality"] = "quality" };
        var steps = new Dictionary<string, string> { ["CollegeCommittee"] = c.Committee, ["Dean"] = c.Dean, ["Quality"] = "quality" };
        if (unit["code"].Str() != c.Code) steps["DepartmentReview"] = c.Departments.First(d => d.Code == unit["code"].Str()).Rep;
        return steps;
    }

    private static string EntryOf(JsonNode unit, College? college) =>
        college?.Entry ?? (unit["code"].Str() == "CU" ? "entry.uni" : "entry.central");

    private async Task<JsonNode> AdvanceAsync(JsonNode form, JsonNode unit, string until)
    {
        var steps = Reviewers(unit);
        while (form["step"].Str() != until && form["status"].Str() != "Approved")
        {
            var step = form["step"].Str()!;
            if (!steps.TryGetValue(step, out var who)) throw new InvalidOperationException($"{unit["code"].Str()}: no demo reviewer for step {step}.");
            form = (await (await UserAsync(who)).CallAsync(HttpMethod.Post, $"/api/forms/{form["id"].Str()}/approve", new JsonObject { ["note"] = "روجعت القيم والأدلة" }, 200, ct)).Body!;
        }
        return form;
    }

    private async Task<JsonNode> ReturnAsync(JsonNode form, JsonNode unit, string note)
    {
        var reviewer = await UserAsync(Reviewers(unit)[form["step"].Str()!]);
        var items = new JsonArray();
        foreach (var i in ItemsOf(form).Where(i => i["value"].Truthy() && i["value"]!["kind"].Str() == "Number").Take(2))
            items.Add(new JsonObject { ["indicatorId"] = i["indicatorId"].Copy(), ["note"] = "الرقم لا يطابق الكشف المرفق؛ راجعوا المصدر" });
        return (await reviewer.CallAsync(HttpMethod.Post, $"/api/forms/{form["id"].Str()}/return", new JsonObject { ["note"] = note, ["items"] = items }, 200, ct)).Body!;
    }

    private async Task RunCycleAsync(CycleSpec spec)
    {
        var planner = await UserAsync("planner");
        var tier1 = await UserAsync("tier1");
        var created = (await planner.CallAsync(HttpMethod.Post, "/api/cycles", new JsonObject
        {
            ["name"] = spec.Name, ["periodLabel"] = null, ["targetYear"] = TargetYear, ["opensOn"] = spec.Opens, ["dueOn"] = spec.Due,
            ["trial"] = false, ["version"] = null, ["quarter"] = spec.Quarter,
        }, 200, ct)).Body!;
        var cycle = (await planner.CallAsync(HttpMethod.Post, $"/api/cycles/{created["id"].Str()}/open", null, 200, ct)).Body!;
        var cycleId = cycle["id"].Str();
        var rows = (await planner.GetAsync($"/api/forms?cycleId={cycleId}", ct)).Items().ToList();
        log.LogInformation("  {Cycle}: {Count} forms", spec.Name, rows.Count);
        var stages = spec.Closed ? null : CurrentStages;
        foreach (var row in rows)
        {
            if (!unitsById.TryGetValue(row["unitId"].Str()!, out var unit)) continue;
            // Only the demo colleges and central units have demo accounts; every other unit's form stays not started.
            if (CollegeOf(unit) is null && !CentralUnits.Contains(unit["code"].Str()!)) continue;
            var stage = stages is not null && stages.TryGetValue(unit["code"].Str()!, out var s) ? s : "Done";
            var college = CollegeOf(unit);
            var entry = await UserAsync(EntryOf(unit, college));
            var form = (await entry.GetAsync($"/api/forms/{row["id"].Str()}", ct))!;
            if (stage == "empty") continue;
            if (stage == "partial")
            {
                await FillAsync(entry, form, unit, spec, Math.Max(1, ItemsOf(form).Count / 2));
                continue;
            }
            await FillAsync(entry, form, unit, spec);
            form = (await entry.CallAsync(HttpMethod.Post, $"/api/forms/{form["id"].Str()}/submit", null, 200, ct)).Body!;
            var returnedOnce = stages is null && spec.Quarter == 2 && unit["code"].Str() == "ENG";
            if (stage == "returned" || returnedOnce)
            {
                form = await ReturnAsync((await entry.GetAsync($"/api/forms/{form["id"].Str()}", ct))!, unit, "يرجى مراجعة مؤشرين قبل الإحالة للعميد");
                if (stage == "returned")
                {
                    await planner.CallAsync(HttpMethod.Post, $"/api/forms/{form["id"].Str()}/extend", new JsonObject { ["dueOn"] = "2027-11-07", ["reason"] = "تمديد أسبوع لتصحيح ملاحظات لجنة الكلية" }, 200, ct);
                    continue;
                }
                await FillAsync(entry, (await entry.GetAsync($"/api/forms/{form["id"].Str()}", ct))!, unit, spec);
                form = (await entry.CallAsync(HttpMethod.Post, $"/api/forms/{form["id"].Str()}/submit", null, 200, ct)).Body!;
            }
            await AdvanceAsync(form, unit, stage == "Done" ? "Done" : stage);
        }
        if (spec.Closed)
        {
            // The Tier 1 file officer transfers the computed values to the university indicators, then the plan manager closes the cycle.
            await tier1.CallAsync(HttpMethod.Post, "/api/review/transfer-all", new JsonObject { ["cycleId"] = cycleId }, ct: ct);
            await planner.CallAsync(HttpMethod.Post, $"/api/cycles/{cycleId}/close", new JsonObject { ["note"] = "انتهت الدورة واعتمدت كل النماذج" }, 200, ct);
        }
    }
}
