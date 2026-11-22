using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Tier1.TestSupport;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// The performance scorecard on a CLEAN database with the demo colleges: an official quarterly cycle for year 1 quarter 1 is filled and approved by every
/// form's own path, the Tier 1 officer transfers the values, then the score and tier are checked against the KPI.xlsx formulas and the workbook is exported.
/// It needs every form to be enterable by a demo account, so run it with Seed:Structure=false and Seed:DemoData=false (a fresh database), after --reset-kpi.
/// Opt in with T1_E2E_SCORECARD=1.
/// </summary>
public sealed class ScorecardTests(ITestOutputHelper output)
{
    private static readonly string[] Entry = ["entry", "entry.cs", "entry.com", "entry.central", "entry.uni", "deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus", "planner"];
    private static readonly string[] Review = ["deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus", "college", "college.cs", "college.com", "dean", "dean.cs", "dean.com", "tier1", "planner"];

    public sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (!Env.ServerUp) Skip = $"No server at {Env.Base}.";
            else if (Environment.GetEnvironmentVariable("T1_E2E_SCORECARD") != "1") Skip = "Needs a clean demo-college database; set T1_E2E_SCORECARD=1.";
        }
    }

    [OptInFact]
    public async Task Quarterly_cycle_scorecard_and_workbook()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");

        t.Step("1. The framework is loaded: 92 approved indicators with five-year targets");
        var seedPath = Path.Combine(Env.RepoRoot(), "src", "Tier1.Infrastructure", "Persistence", "Seed", "kpi-framework.json");
        var framework = JsonNode.Parse(File.ReadAllText(seedPath))!["indicators"].Items().Select(i => i["code"].Str()!).ToHashSet();
        var indicators = (await planner.Get("/api/kpi/indicators")).Items().ToDictionary(i => i["code"].Str()!);
        t.Check(framework.Count == 92 && framework.IsSubsetOf(indicators.Keys), $"All 92 framework indicators exist (database has {indicators.Count})");
        t.Check(framework.All(c => indicators[c]["status"].Str() == "Approved"), "All framework definitions are approved");
        var tree = (await planner.Get("/api/strategy/tree"))!;
        t.Check(tree["pillars"].Items().Count() == 6 && tree["pillars"].Items().Sum(p => p["standards"].Items().Count()) == 12, "6 pillars and 12 standards");
        var i703 = tree["pillars"].Items().SelectMany(p => p["standards"].Items()).SelectMany(s => s["indicators"].Items()).First(i => i["code"].Str() == "7.03");
        t.Check(i703["direction"].Str() == "LowerIsBetter", "The 7.03 correction (descending) is applied");

        t.Step("2. The quarter calendar and one official quarterly cycle per quarter");
        var cal = (await planner.Get("/api/plan/calendar")).Items().ToList();
        t.Check(cal.Count == 20 && cal[0]["label"].Str()!.StartsWith("السنة 1", StringComparison.Ordinal), "Twenty quarters starting with year 1");
        var body = Scenario.O(("name", "دورة السنة 1 الربع 1"), ("periodLabel", null), ("targetYear", 2027), ("quarter", 1), ("trial", false), ("opensOn", Calls.Today()), ("dueOn", Calls.Today(30)), ("version", null));
        var existing = (await planner.Get("/api/cycles")).Items().FirstOrDefault(c => c["quarter"].Num() == 1 && c["targetYear"].Num() == 2027 && !c["trial"].Truthy());
        JsonNode cycle;
        if (existing is not null) { cycle = existing; t.Check(true, "The quarter's cycle exists from an earlier run"); }
        else
        {
            cycle = await planner.Ok("POST", "/api/cycles", body);
            t.Check(cycle["periodLabel"].Str()!.StartsWith("السنة 1", StringComparison.Ordinal), $"The period is named automatically: {cycle["periodLabel"].Str()}");
        }
        var (status, err) = await planner.Call("POST", "/api/cycles", body);
        t.Check(status is 400 or 409 or 422 && err.Code() == "quarter_taken", "A second official cycle for the same quarter is refused");
        if (cycle["status"].Str() == "Draft") cycle = await planner.Ok("POST", $"/api/cycles/{cycle["id"].Str()}/open");
        t.Check(cycle["status"].Str() == "Open", "The cycle is open");
        var cycleId = cycle["id"].Str()!;

        t.Step("3. Enter every form and approve it to the university Tier 1 committee");
        var forms = (await planner.Get($"/api/forms?cycleId={cycleId}")).Items().ToList();
        t.Check(forms.Count > 0, $"{forms.Count} forms in the cycle");
        var approved = 0;
        foreach (var row in forms)
        {
            var id = row["id"].Str();
            if (row["status"].Str() == "Approved") { approved++; continue; }
            DemoClient? filler = null; JsonNode? form = null;
            foreach (var name in Entry)
            {
                var (s, f) = await (await Env.As(name)).Call("GET", $"/api/forms/{id}");
                if (s == 200 && f!["actions"]!["canEdit"].Truthy()) { filler = await Env.As(name); form = f; break; }
            }
            if (filler is not null && form is not null)
            {
                foreach (var item in form.ItemsOf().Where(i => !i["value"].Truthy()).ToList())
                {
                    var b = Calls.InputFor(item);
                    var (s, saved) = await filler.Call("PUT", $"/api/forms/{id}/values", b);
                    if (s == 200 && saved!["issues"].Items().Any(x => x["code"].Str() == "big_change"))
                        await filler.Ok("PUT", $"/api/forms/{id}/values", Scenario.With(b, ("explanation", "قيمة الربع الأول"), ("version", saved["value"]!["version"])));
                }
                await filler.Ok("POST", $"/api/forms/{id}/submit", await filler.Versioned($"/api/forms/{id}"));
            }
            for (var i = 0; i < 6; i++)
            {
                if ((await planner.Get($"/api/forms/{id}"))!["status"].Str() == "Approved") break;
                var done = false;
                foreach (var name in Review)
                {
                    var reviewer = await Env.As(name);
                    var (s, f) = await reviewer.Call("GET", $"/api/forms/{id}");
                    if (s == 200 && f!["actions"]!["canApprove"].Truthy()) { await reviewer.Ok("POST", $"/api/forms/{id}/approve", await reviewer.Versioned($"/api/forms/{id}", Scenario.O(("note", null)))); done = true; break; }
                }
                if (!done) break;
            }
            if ((await planner.Get($"/api/forms/{id}"))!["status"].Str() == "Approved") approved++;
            else output.WriteLine($"   (approval of {row["unitName"].Str()} did not complete)");
        }
        t.Check(approved == forms.Count, $"Approved {approved} of {forms.Count} forms");

        t.Step("3b. The Tier 1 officer reviews the college indicators and transfers the values to the university indicators");
        var officer = await Env.As("tier1");
        var college = await Env.As("college");
        (status, _) = await college.Call("GET", $"/api/review?cycleId={cycleId}");
        t.Check(status == 403, "The college officer does not see the review screen");
        var review = (await officer.Get($"/api/review?cycleId={cycleId}"))!;
        t.Check(review["canTransfer"].Truthy() && review["cycle"]!["id"].Str() == cycleId, "The review is on the quarter's cycle and transfer is allowed");
        var rows = review["rows"].Items().ToDictionary(r => r["code"].Str()!);
        t.Check(framework.IsSubsetOf(rows.Keys), $"Every framework indicator is in the review ({rows.Count})");
        var r101 = rows["1.01"];
        t.Check(Math.Abs((double)r101["computed"].Num()! - 90) < 0.01 && r101["reported"].Num() == r101["expected"].Num(), $"1.01 computes 90 from {r101["expected"].Num()} units");
        t.Check(r101["units"].Items().Count() >= 2 && r101["units"].Items().All(u => u["value"].Num() is not null && u["numerator"].Num() is not null), "Each college's value with its numerator and denominator");
        t.Check(r101["units"].Items().All(u => u["universityAchievement"].Num() is not null), "Each college's share of the university target");
        var r106 = rows["1.06"];
        t.Check(!r106["comparable"].Truthy() && r106["units"].Items().All(u => u["universityAchievement"].Num() is null), "The sum indicator 1.06 does not compare a college to the whole-university target");
        t.Check(Math.Abs((double)r106["units"].Items().Sum(u => u["share"].Num() ?? 0) - 100) < 0.5, "The colleges' shares of the university number total 100%");
        (status, err) = await officer.Call("POST", "/api/review/transfer", Scenario.O(("cycleId", cycleId), ("indicatorId", r101["indicatorId"]), ("value", 91), ("note", null), ("version", null)));
        t.Check(status == 400 && err.Code() == "note_required", "Changing the value from the computed one needs a reason");
        (status, _) = await planner.Call("POST", "/api/review/transfer", Scenario.O(("cycleId", cycleId), ("indicatorId", r101["indicatorId"]), ("value", null), ("note", null), ("version", null)));
        t.Check(status == 403, "The planner does not transfer values");
        var one = await officer.Ok("POST", "/api/review/transfer", Scenario.O(("cycleId", cycleId), ("indicatorId", r101["indicatorId"]), ("value", null), ("note", null), ("version", r101["official"].Truthy() ? r101["official"]!["version"] : null)));
        t.Check(one["state"].Str() == "Transferred" && Math.Abs((double)one["official"]!["value"].Num()! - 90) < 0.01, "1.01 was transferred to the university indicator");
        var moved = await officer.Ok("POST", "/api/review/transfer-all", Scenario.O(("cycleId", cycleId)));
        review = (await officer.Get($"/api/review?cycleId={cycleId}"))!;
        var pending = review["rows"].Items().Where(r => r["computed"].Num() is not null && r["state"].Str() != "Transferred").Select(r => r["code"].Str()).ToList();
        t.Check(pending.Count == 0, $"Everything ready was transferred ({moved["transferred"].Num()}); still pending: {string.Join(",", pending.Take(5))}");

        t.Step("3c. A college's progress against the university target");
        var prog = (await college.Get("/api/progress"))!;
        t.Check(prog["units"].Items().Count() == 1, $"The college officer sees their college only: {prog["unit"]!["name"].Str()}");
        var p101 = prog["pillars"].Items().SelectMany(p => p["standards"].Items()).SelectMany(s => s["indicators"].Items()).First(i => i["code"].Str() == "1.01");
        t.Check(p101["latest"].Num() is not null && p101["yearTarget"].Num() is not null && Math.Abs((double)(p101["gap"].Num()! - (p101["latest"].Num()! - p101["yearTarget"].Num()!))) < 0.01,
            $"1.01 for the college {p101["latest"].Num()} against the university target {p101["yearTarget"].Num()}");
        t.Check(p101["universityQuarters"]![0].Num() is { } uq && Math.Abs((double)uq - 90) < 0.01, "The official university value beside the college's");
        var p106 = prog["pillars"].Items().SelectMany(p => p["standards"].Items()).SelectMany(s => s["indicators"].Items()).First(i => i["code"].Str() == "1.06");
        t.Check(!p106["comparable"].Truthy() && p106["share"].Num() is not null && (p106["ownTarget"].Num() is not null || p106["score"].Num() is null), $"1.06 (sum) for the college: its share {p106["share"].Num()}% of the university number, not compared to the whole-university target");
        var whole = (await planner.Get("/api/progress"))!;
        t.Check(whole["units"].Items().Count() >= 2 && whole["universityScore"].Num() is not null, $"The planner sees {whole["units"].Items().Count()} colleges and the university score");

        t.Step("4. The score and tier by the workbook's formulas");
        var card = (await planner.Get("/api/scorecard"))!;
        t.Check(card["lastReported"].Truthy() && card["lastReported"]!["yearIndex"].Num() == 1 && card["lastReported"]!["quarter"].Num() == 1, "The last approved quarter: year 1 quarter 1");
        var all = card["pillars"].Items().SelectMany(p => p["standards"].Items()).SelectMany(s => s["indicators"].Items()).ToDictionary(i => i["code"].Str()!);
        var i101 = all["1.01"];
        t.Check(Math.Abs((double)i101["quarters"]![0].Num()! - 90) < 0.01, $"The university value for quarter 1 of 1.01 = {i101["quarters"]![0].Num()}");
        t.Check(i101["score"].Num() == 100 && i101["level"].Str() == "InLevel", "1.01 is achieved at 100% and in level");
        t.Check(i101["yearScoresVsYearly"]![0].Num() == 100, "Against the year-1 target (82): 100%");
        foreach (var p in card["pillars"].Items())
        {
            foreach (var s in p["standards"].Items())
            {
                var expected = Math.Round((double)s["indicators"].Items().Sum(i => i["score"].Num() ?? 0) / s["indicators"].Items().Count() + 1e-9, 1);
                t.Check(Math.Abs((double)s["score"].Num()! - expected) < 0.051, $"Standard {s["code"].Str()} score = {s["score"].Num()}");
            }
            var expP = Math.Round((double)p["standards"].Items().Sum(s => s["score"].Num() ?? 0) / p["standards"].Items().Count() + 1e-9, 1);
            t.Check(Math.Abs((double)p["score"].Num()! - expP) < 0.051, $"Pillar {p["code"].Str()} score = {p["score"].Num()} (the mean of its standards)");
        }
        var expO = Math.Round((double)card["pillars"].Items().Sum(p => p["score"].Num() ?? 0) / card["pillars"].Items().Count() + 1e-9, 1);
        var o = card["overall"]!;
        t.Check(Math.Abs((double)o["score"].Num()! - expO) < 0.051, $"The overall score {o["score"].Num()} = the mean of the pillars");
        var th = card["thresholds"]!;
        int TierOf(double v, double off) => v >= (double)th["tier1"].Num()! + off ? 1 : v >= (double)th["tier2"].Num()! + off ? 2 : v >= (double)th["tier3"].Num()! + off ? 3 : v >= (double)th["tier4"].Num()! + off ? 4 : 5;
        var overallTier = TierOf((double)o["score"].Num()!, (double)th["overallOffset"].Num()!);
        var weakest = TierOf(card["pillars"].Items().Min(p => (double)p["score"].Num()!), 0);
        var final = Math.Min(weakest - overallTier >= 2 ? overallTier + 1 : overallTier, 5);
        t.Check(o["tier"]!["finalTier"].Num() == final, $"The final tier {final} (overall {overallTier}, weakest pillar {weakest})");
        t.Check(card["perspectives"].Items().Count() == 7 && card["sdgs"].Items().Count() == 17, "Seven perspectives and 17 goals");

        t.Step("5. The council workbook KPI.xlsx with the values");
        var dl = await planner.Download("/api/scorecard/kpi.xlsx");
        t.Check(dl.Status == 200 && dl.ContentType.Contains("spreadsheet", StringComparison.Ordinal), "The file downloaded");
        using var wb = new XLWorkbook(new MemoryStream(dl.Body));
        var ws = wb.Worksheets.First(w => w.Name.Contains("بطاقة مؤشرات الأداء", StringComparison.Ordinal));
        t.Check(Math.Abs(ws.Cell("U10").GetDouble() - 90) < 0.01, $"Cell U10 of indicator 1.01 = {ws.Cell("U10").GetString()}");
        t.Check(ws.Cell("V10").IsEmpty(), "The second quarter is empty");
        t.Check(ws.Cell("AS10").HasFormula, "The workbook's formulas are kept as they are");
        t.Check(wb.Worksheets.Count == 8, "All eight sheets are in the file");
        t.Finish();
    }
}

/// <summary>
/// Load test: concurrent users repeat what a real user does in a cycle (home, form list, open a form, results, the dictionary) and the response time
/// (median, p95, max) and server-error count are measured per route. Accepted: p95 under two seconds and no server errors.
/// Opt in with T1_E2E_LOAD=1 (T1_LOAD_USERS default 100, T1_LOAD_SECONDS default 60).
/// </summary>
public sealed class LoadTests(ITestOutputHelper output)
{
    private static readonly string[] Accounts = ["planner", "tier1", "dean", "college", "entry", "deptrep", "viewer", "vp", "president", "committee"];

    public sealed class LoadFactAttribute : FactAttribute
    {
        public LoadFactAttribute()
        {
            if (!Env.ServerUp) Skip = $"No server at {Env.Base}.";
            else if (Environment.GetEnvironmentVariable("T1_E2E_LOAD") != "1") Skip = "Set T1_E2E_LOAD=1 to run the load test.";
        }
    }

    [LoadFact]
    public async Task Concurrent_users_stay_under_two_seconds()
    {
        var users = int.Parse(Environment.GetEnvironmentVariable("T1_LOAD_USERS") ?? "100", System.Globalization.CultureInfo.InvariantCulture);
        var seconds = int.Parse(Environment.GetEnvironmentVariable("T1_LOAD_SECONDS") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
        var timings = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<double>>();
        var errors = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

        async Task<(int, JsonNode?)> Timed(DemoClient c, string name, string path)
        {
            var start = Stopwatch.GetTimestamp();
            var result = await c.CallAsync(HttpMethod.Get, path, null, null, default);
            timings.GetOrAdd(name, _ => []).Add(Stopwatch.GetElapsedTime(start).TotalSeconds);
            if (result.Status >= 500) errors.AddOrUpdate(name, 1, (_, n) => n + 1);
            return result;
        }

        output.WriteLine($"Logging in {users} virtual users…");
        var clients = new DemoClient?[users];
        await Parallel.ForEachAsync(Enumerable.Range(0, users), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (i, ct) =>
        {
            for (var attempt = 0; attempt < 5 && clients[i] is null; attempt++)
            {
                try { clients[i] = await DemoClient.LoginAsync(Env.Base, $"{Accounts[i % Accounts.Length]}@tier1.local", Env.Password, ct); }
                catch (InvalidOperationException) { await Task.Delay(1000 * (attempt + 1), ct); } // simultaneous logins can hit the rate limit
            }
            if (clients[i] is null) errors.AddOrUpdate("login", 1, (_, n) => n + 1);
        });

        output.WriteLine($"Running for {seconds}s…");
        var pool = await PoolWatch.StartAsync();
        var until = DateTime.UtcNow.AddSeconds(seconds);
        await Task.WhenAll(clients.Where(c => c is not null).Select(async c =>
        {
            var rnd = new Random();
            while (DateTime.UtcNow < until)
            {
                await Timed(c!, "home", "/api/home");
                var (status, forms) = await Timed(c!, "forms", "/api/forms");
                if (status == 200 && forms.Items().Any()) await Timed(c!, "form", $"/api/forms/{forms.Items().ElementAt(rnd.Next(forms.Items().Count()))["id"].Str()}");
                await Timed(c!, "results", "/api/results");
                await Timed(c!, "indicators", "/api/kpi/indicators");
                await Task.Delay(rnd.Next(500, 2000)); // the user's thinking time between pages
            }
        }));

        var total = timings.Values.Sum(v => v.Count);
        output.WriteLine($"{"route",-12}{"requests",10}{"median ms",12}{"p95 ms",10}{"max ms",10}{"5xx",6}");
        var worst = 0.0;
        foreach (var name in new[] { "home", "forms", "form", "results", "indicators" })
        {
            if (!timings.TryGetValue(name, out var bag)) continue;
            var v = bag.Order().ToList();
            var p95 = v[Math.Min(v.Count - 1, (int)(v.Count * 0.95))];
            worst = Math.Max(worst, p95);
            output.WriteLine($"{name,-12}{v.Count,10}{v[v.Count / 2] * 1000,12:0}{p95 * 1000,10:0}{v[^1] * 1000,10:0}{errors.GetValueOrDefault(name),6}");
        }
        var failed = errors.Values.Sum();
        output.WriteLine($"{total} requests, {total / (double)seconds:0.0} req/s, worst p95 {worst * 1000:0} ms, server errors {failed}");
        output.WriteLine(await pool.StopAsync());
        Assert.True(worst < 2.0 && failed == 0, $"p95 {worst * 1000:0} ms (limit 2000), server errors {failed}.");
    }

    /// <summary>
    /// PERF-B11: deadline day, with writes. In a trial cycle of its own:
    /// - 70% of the virtual users are data entry: many at once on the same college form, each autosaving its own share of the
    ///   indicators. Their shares do not overlap, so a 409 here would be a false conflict (DB-1) and fails the test.
    /// - 10% are reviewers approving, step by step, the department forms filled and sent before the clock starts.
    /// - 20% open the dashboards: home, the forms list, results, an indicator's trend, and a college's progress.
    /// p50, p95 and p99, server errors and conflicts are reported per route. Accepted: p95 under two seconds on every route,
    /// no server error, and no conflict on saves. Opt in with T1_E2E_LOAD=1 (T1_LOAD_USERS default 60, T1_LOAD_SECONDS default 60).
    /// </summary>
    [LoadFact]
    public async Task Deadline_day_saves_approvals_and_dashboards_stay_under_two_seconds()
    {
        var users = int.Parse(Environment.GetEnvironmentVariable("T1_LOAD_USERS") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
        var seconds = int.Parse(Environment.GetEnvironmentVariable("T1_LOAD_SECONDS") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
        var stats = new LoadStats();
        PoolWatch? pool = null;

        // The cycle, and the department forms filled and sent for review before the clock starts.
        var planner = await Env.As("planner");
        var tag = Calls.Tag();
        var created = await planner.Ok("POST", "/api/cycles", Scenario.O(("name", $"اختبار الحمل {tag}"), ("periodLabel", $"load-{tag}"), ("targetYear", 2027),
            ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", true), ("version", null)));
        var cycleId = created["id"].Str()!;
        await planner.Ok("POST", $"/api/cycles/{cycleId}/open");
        try
        {
            // A department form is entered by the college's data entry and reviewed first by the department representative.
            output.WriteLine("Filling and sending the department forms…");
            var writerAccounts = new[] { "entry", "entry.cs", "entry.com" };
            foreach (var account in writerAccounts)
            {
                var client = await TryLoginAsync(account);
                if (client is null) continue;
                foreach (var row in (await client.Get($"/api/forms?cycleId={cycleId}&awaitingMe=true")).Items().Where(r => r["unitKind"].Str() == "Department"))
                {
                    var form = await client.Fill((await client.Get($"/api/forms/{row["id"].Str()}"))!, tag);
                    await client.Ok("POST", $"/api/forms/{form["id"].Str()}/submit", Scenario.O(("version", form["version"])));
                }
            }

            // Every step of the path has its reviewer: department representatives, college committees, deans, the university Tier 1 committee.
            var reviewerAccounts = new[] { "deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus", "college", "college.cs", "college.com", "dean", "dean.cs", "dean.com", "tier1" };
            var dashboardAccounts = new[] { "president", "vp", "planner", "viewer" };
            var writers = (int)Math.Round(users * 0.7);
            var reviewers = Math.Clamp((int)Math.Round(users * 0.1), 1, reviewerAccounts.Length);
            var dashboards = users - writers - reviewers;
            output.WriteLine($"Logging in {users} virtual users ({writers} entering, {reviewers} reviewing, {dashboards} on dashboards)…");
            var writing = await LoginAllAsync(Enumerable.Range(0, writers).Select(i => writerAccounts[i % writerAccounts.Length]));
            var reviewing = await LoginAllAsync(reviewerAccounts);
            var watching = await LoginAllAsync(Enumerable.Range(0, dashboards).Select(i => dashboardAccounts[i % dashboardAccounts.Length]));
            stats.Error("login", writing.Concat(reviewing).Concat(watching).Count(c => c is null));

            var until = DateTime.UtcNow.AddSeconds(seconds);
            output.WriteLine($"Running for {seconds}s…");
            pool = await PoolWatch.StartAsync();
            await Task.WhenAll(
            [
                // Users of one account share its form: the k-th of n autosaves every n-th indicator, from k.
                .. writing.Select((c, i) => (c, i)).Where(x => x.c is not null).Select(x =>
                    WriteAsync(x.c!, stats, cycleId, x.i / writerAccounts.Length, (writers - 1 - x.i % writerAccounts.Length) / writerAccounts.Length + 1, until)),
                // Each reviewing user acts for its share of the reviewer accounts, so no two users approve the same form.
                .. Enumerable.Range(0, reviewers).Select(r =>
                    ReviewAsync(reviewing.Where((c, i) => c is not null && i % reviewers == r).Select(c => c!).ToList(), stats, cycleId, until)),
                .. watching.Where(c => c is not null).Select(c => DashboardsAsync(c!, stats, cycleId, until)),
            ]);
        }
        finally
        {
            await planner.Call("POST", $"/api/cycles/{cycleId}/close", Scenario.O(("note", "انتهى اختبار الحمل")));
        }

        var (worst, failed, conflicts) = stats.Report(output, seconds);
        if (pool is not null) output.WriteLine(await pool.StopAsync());
        Assert.True(worst < 2.0 && failed == 0 && conflicts == 0, $"p95 {worst * 1000:0} ms (limit 2000), server errors {failed}, conflicts on saves {conflicts}.");
    }

    private static async Task<DemoClient?[]> LoginAllAsync(IEnumerable<string> accounts)
    {
        var list = accounts.ToList();
        var clients = new DemoClient?[list.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, list.Count), new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (i, _) => clients[i] = await TryLoginAsync(list[i]));
        return clients;
    }

    private static async Task<DemoClient?> TryLoginAsync(string account)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { return await DemoClient.LoginAsync(Env.Base, $"{account}@tier1.local", Env.Password, default); }
            catch (InvalidOperationException) { await Task.Delay(1000 * (attempt + 1)); } // simultaneous logins can hit the rate limit
        }
        return null;
    }

    /// <summary>Autosaves this user's share of the indicators of the college form awaiting the account, as the entry screen does.</summary>
    private static async Task WriteAsync(DemoClient c, LoadStats stats, string cycleId, int index, int of, DateTime until)
    {
        var rnd = new Random();
        var (_, list) = await stats.Timed(c, "forms", HttpMethod.Get, $"/api/forms?cycleId={cycleId}&awaitingMe=true");
        var row = list.Items().FirstOrDefault(r => r["unitKind"].Str() != "Department");
        if (row is null) return;
        var path = $"/api/forms/{row["id"].Str()}";
        var (_, form) = await stats.Timed(c, "form", HttpMethod.Get, path);
        var mine = (form?.ItemsOf() ?? []).Where((_, n) => n % of == index).ToList();
        if (mine.Count == 0) return;
        var versions = mine.ToDictionary(item => item["indicatorId"].Str()!, item => item["value"].Truthy() ? item["value"]!["version"].Copy() : null);
        var step = 0;
        while (DateTime.UtcNow < until)
        {
            var item = mine[step % mine.Count];
            var body = Calls.InputFor(item);
            body["version"] = versions[item["indicatorId"].Str()!]?.DeepClone();
            if (body["value"] is not null && !item["usesRatio"].Truthy()) body["value"] = 10 + step % 5;
            var (status, saved) = await stats.Timed(c, "save", HttpMethod.Put, $"{path}/values", body);
            if (status == 200) versions[item["indicatorId"].Str()!] = saved!["value"]!["version"].Copy();
            if (++step % 8 == 0) await stats.Timed(c, "form", HttpMethod.Get, path);
            await Task.Delay(rnd.Next(300, 1500)); // typing between two autosaves
        }
    }

    /// <summary>Approves, one decision at a time, a form awaiting one of the user's reviewer accounts at that account's step.</summary>
    private static async Task ReviewAsync(List<DemoClient> accounts, LoadStats stats, string cycleId, DateTime until)
    {
        var rnd = new Random();
        for (var turn = 0; accounts.Count > 0 && DateTime.UtcNow < until; turn++)
        {
            var c = accounts[turn % accounts.Count];
            var (_, list) = await stats.Timed(c, "forms", HttpMethod.Get, $"/api/forms?cycleId={cycleId}&awaitingMe=true");
            var waiting = list.Items().Where(r => r["status"].Str() == "InReview").ToList();
            // A form can await the account for a direct approval past an earlier step; the reviewer approves only at its own step.
            foreach (var row in waiting)
            {
                var path = $"/api/forms/{row["id"].Str()}";
                var (_, form) = await stats.Timed(c, "form", HttpMethod.Get, path);
                if (!form?["actions"]?["canApprove"].Truthy() ?? true) continue;
                await stats.Timed(c, "approve", HttpMethod.Post, $"{path}/approve", Scenario.O(("note", "مراجعة اختبار الحمل"), ("version", form!["version"].Copy()), ("direct", false)));
                break;
            }
            await Task.Delay(rnd.Next(2000, 5000)); // reading the form before the next decision
        }
    }

    private static async Task DashboardsAsync(DemoClient c, LoadStats stats, string cycleId, DateTime until)
    {
        var rnd = new Random();
        while (DateTime.UtcNow < until)
        {
            await stats.Timed(c, "home", HttpMethod.Get, "/api/home");
            await stats.Timed(c, "forms", HttpMethod.Get, $"/api/forms?cycleId={cycleId}");
            var (status, results) = await stats.Timed(c, "results", HttpMethod.Get, $"/api/results?cycleId={cycleId}");
            if (status == 200 && results?["indicators"].Items().ElementAtOrDefault(rnd.Next(10)) is { } indicator)
                await stats.Timed(c, "trend", HttpMethod.Get, $"/api/results/{cycleId}/indicators/{indicator["indicatorId"].Str()}");
            await stats.Timed(c, "progress", HttpMethod.Get, "/api/progress");
            await Task.Delay(rnd.Next(500, 2000));
        }
    }

    /// <summary>Times per route, with server errors and conflicts (409 on a save means two users' edits collided).</summary>
    private sealed class LoadStats
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<double>> timings = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> errors = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> conflicts = new();

        /// <summary>Business refusals by route, status and code, so an unexpected 4xx is explained in the report.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> refusals = new();

        public void Error(string route, int count)
        {
            if (count > 0) errors.AddOrUpdate(route, count, (_, n) => n + count);
        }

        public async Task<(int Status, JsonNode? Body)> Timed(DemoClient c, string route, HttpMethod method, string path, JsonNode? body = null)
        {
            var start = Stopwatch.GetTimestamp();
            var result = await c.CallAsync(method, path, body, null, default);
            timings.GetOrAdd(route, _ => []).Add(Stopwatch.GetElapsedTime(start).TotalSeconds);
            if (result.Status >= 500) Error(route, 1);
            if (result.Status == 409) conflicts.AddOrUpdate(route, 1, (_, n) => n + 1);
            if (result.Status is >= 400 and < 500) refusals.AddOrUpdate($"{route} {result.Status} {result.Body.Code()}", 1, (_, n) => n + 1);
            return result;
        }

        public (double WorstP95, int ServerErrors, int SaveConflicts) Report(ITestOutputHelper output, int seconds)
        {
            static double At(List<double> v, double q) => v[Math.Min(v.Count - 1, (int)(v.Count * q))] * 1000;
            var total = timings.Values.Sum(v => v.Count);
            output.WriteLine($"{"route",-10}{"requests",10}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}{"max ms",9}{"5xx",6}{"409",6}");
            var worst = 0.0;
            foreach (var (route, bag) in timings.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                var v = bag.Order().ToList();
                worst = Math.Max(worst, At(v, 0.95) / 1000);
                output.WriteLine($"{route,-10}{v.Count,10}{At(v, 0.5),9:0}{At(v, 0.95),9:0}{At(v, 0.99),9:0}{v[^1] * 1000,9:0}{errors.GetValueOrDefault(route),6}{conflicts.GetValueOrDefault(route),6}");
            }
            foreach (var (refusal, count) in refusals.OrderBy(r => r.Key, StringComparer.Ordinal)) output.WriteLine($"  refused: {refusal} x{count}");
            var failed = errors.Values.Sum();
            output.WriteLine($"{total} requests, {total / (double)seconds:0.0} req/s, worst p95 {worst * 1000:0} ms, server errors {failed}, conflicts {conflicts.Values.Sum()}");
            return (worst, failed, conflicts.GetValueOrDefault("save"));
        }
    }
}

/// <summary>
/// The server's PostgreSQL connection pool during a load run (Npgsql's own metrics at /api/logs/database, read as the system
/// admin twice a second): peak connections in use against the maximum, and requests left waiting for one. Waiting requests mean
/// the pool, not the code, sets the response time. Reported only; the run passes or fails on its response times.
/// </summary>
internal sealed class PoolWatch
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private int peakUsed, peakPending, max;
    private long timeouts;
    private bool seen;

    private PoolWatch(DemoClient? admin) => loop = admin is null ? Task.CompletedTask : RunAsync(admin);

    public static async Task<PoolWatch> StartAsync()
    {
        DemoClient? admin = null;
        try { admin = await DemoClient.LoginAsync(Env.Base, "sysadmin@tier1.local", Env.Password, default); }
        catch (InvalidOperationException) { } // no system admin account on this server: the report says so
        return new PoolWatch(admin);
    }

    private async Task RunAsync(DemoClient admin)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var (status, pool) = await admin.CallAsync(HttpMethod.Get, "/api/logs/database", null, null, stop.Token);
                if (status == 200 && pool is not null)
                {
                    seen = true;
                    peakUsed = Math.Max(peakUsed, (int)(pool["used"].Num() ?? 0));
                    peakPending = Math.Max(peakPending, (int)(pool["pending"].Num() ?? 0));
                    max = (int)(pool["max"].Num() ?? max);
                    timeouts = (long)(pool["timeouts"].Num() ?? 0);
                }
                await Task.Delay(500, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { admin.Dispose(); }
    }

    public async Task<string> StopAsync()
    {
        await stop.CancelAsync();
        await loop;
        stop.Dispose();
        return seen
            ? $"PostgreSQL pool: peak {peakUsed} connections in use of {max}, peak {peakPending} requests waiting, {timeouts} timeouts since the server started"
            : "PostgreSQL pool: not reported (the server has no /api/logs/database, or no system admin account)";
    }
}
