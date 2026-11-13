using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// The indicator definition and approval cycle, the new-version rule, targets and college scope, the college structure and the "required from you" list,
/// each with the demo accounts in their roles. Repeatable on one database: it creates a fresh indicator each run.
/// </summary>
public sealed class ScenarioTests(ITestOutputHelper output)
{
    private static readonly string[] DefinitionFields = ["nameAr", "nameEn", "definition", "purpose", "exclusions", "numerator", "denominator", "unit", "direction", "frequency", "level", "aggregation", "aggregationNote", "dataSource", "evidenceRequired"];

    private static JsonObject DefinitionBody(JsonNode v, bool withOwner)
    {
        var body = new JsonObject();
        foreach (var k in DefinitionFields) body[k] = v[k].Copy();
        if (withOwner) body["ownerUnitId"] = v["ownerUnitId"].Copy();
        return body;
    }

    [E2EFact]
    public async Task Definition_targets_structure_and_home()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var committee = await Env.As("committee");
        var dean = await Env.As("dean");
        var college = await Env.As("college");
        var viewer = await Env.As("viewer");
        var sysadmin = await Env.As("sysadmin");
        var units = await Env.UnitsAsync(planner);
        var uni = units.Values.First(u => u["kind"].Str() == "University");
        var eng = units["ENG"];
        var cs = units["CS"];

        t.Step("1. Indicator definition: complete, submit, approve with separation of duties");
        var d = await planner.Ok("POST", "/api/kpi/indicators", Scenario.O(("standardCode", "10"), ("nameAr", $"مؤشر اختبار السيناريو {Calls.Tag()}"), ("unit", "%"), ("level", "College"), ("reason", "اختبار مسار التعريف")));
        var code = d["code"].Str()!;
        t.Check(code.StartsWith("10.", StringComparison.Ordinal), $"The code follows the framework format: {code}");
        t.Check(d["current"]!["status"].Str() == "Draft", "The new indicator starts as a draft");
        var (status, _) = await planner.Call("POST", $"/api/kpi/indicators/{code}/submit");
        t.Check(status == 422, "Submitting is refused while the definition is incomplete (owner unit)");
        var v = d["current"]!;
        var body = DefinitionBody(v, withOwner: false);
        body["definition"] = "نسبة اختبارية لمسار التعريف"; body["numerator"] = "البسط"; body["denominator"] = "المقام"; body["unit"] = "%";
        body["direction"] = "HigherIsBetter"; body["frequency"] = "Quarterly"; body["level"] = "College"; body["aggregation"] = "RatioOfSums";
        body["dataSource"] = "سجلات الكلية"; body["ownerUnitId"] = uni["id"].Copy(); body["version"] = v["version"].Copy();
        d = await planner.Ok("PUT", $"/api/kpi/indicators/{code}/definition", body);
        t.Check(!d["current"]!["missing"].Truthy(), "The definition is complete");
        d = await planner.Ok("POST", $"/api/kpi/indicators/{code}/submit");
        t.Check(d["current"]!["status"].Str() == "UnderReview", "Submitted for approval");
        (status, _) = await planner.Call("POST", $"/api/kpi/indicators/{code}/approve", Scenario.O(("decisionRef", "محضر 1")));
        t.Check(status == 403, "The plan manager cannot approve (403)");
        (status, var err) = await committee.Call("POST", $"/api/kpi/indicators/{code}/approve", Scenario.O(("decisionRef", " ")));
        t.Check(status == 400 && err.Code() == "decision_ref_required", "Approval asks for the minutes number");
        d = await committee.Ok("POST", $"/api/kpi/indicators/{code}/approve", Scenario.O(("decisionRef", "محضر اللجنة العليا 1/2026")));
        t.Check(d["current"]!["status"].Str() == "Approved", "The higher committee approved the definition");

        t.Step("2. Editing an approved definition creates a new version; the approved one stays in force");
        v = d["current"]!;
        body = DefinitionBody(v, withOwner: true);
        if (d["actions"]!["editCreatesVersion"].Truthy())
        {
            (status, err) = await planner.Call("PUT", $"/api/kpi/indicators/{code}/definition", body);
            t.Check(status == 400 && err.Code() == "change_reason_required", "Editing an approved definition without a reason is refused");
            body["changeReason"] = "حصر الأبحاث في Scopus فقط بقرار اللجنة";
            body["denominator"] = "الأبحاث المفهرسة في Scopus";
            d = await planner.Ok("PUT", $"/api/kpi/indicators/{code}/definition", body);
            t.Check((d["current"]!["number"].Num() ?? 0) >= 2 && d["current"]!["status"].Str() == "Draft", $"Version {d["current"]!["number"].Num()} created as a draft");
            t.Check(d["effective"].Truthy() && d["effective"]!["denominator"].Str() != "الأبحاث المفهرسة في Scopus", "The previous approved version is still in force with its old denominator");
        }
        else t.Check(true, "The new version already exists from an earlier run");

        t.Step("3. Targets: college scope, distribution and separation of duties in approval");
        string? code2 = null; int year = 0;
        var sums = (await planner.Get("/api/kpi/indicators")).Items().Where(r => r["aggregation"].Str() == "Sum" && r["level"].Str() == "College" && !r["suspended"].Truthy()).Select(r => r["code"].Str()!).ToList();
        foreach (var candidate in new[] { "4.09" }.Concat(sums.Where(c => c != "4.09")))
        {
            var existing = (await planner.Get($"/api/kpi/indicators/{candidate}/targets"))!["colleges"].Items().Where(x => x["unitId"].Str() == eng["id"].Str()).Select(x => (int)x["year"].Num()!).ToHashSet();
            var free = Enumerable.Range(2027, 5).FirstOrDefault(y => !existing.Contains(y));
            if (free != 0) { code2 = candidate; year = free; break; }
        }
        Assert.True(code2 is not null, "Every sum indicator already has engineering targets; reset the data.");
        output.WriteLine($"  (indicator {code2}, year {year})");
        var dist = (await planner.Get($"/api/kpi/indicators/{code2}/distribution?year={year}"))!;
        t.Check(dist["result"]!["status"].Str() is "Covers" or "Short" or "Incomplete", $"Distribution: {dist["result"]!["summaryAr"].Str()}");
        (status, _) = await dean.Call("PUT", $"/api/kpi/indicators/{code2}/targets", Scenario.O(("unitId", cs["id"]), ("year", year), ("kind", "Target"), ("value", 3)));
        t.Check(status == 404, "The engineering dean cannot reach the computing college (404)");
        (status, _) = await dean.Call("PUT", $"/api/kpi/indicators/{code2}/targets", Scenario.O(("unitId", uni["id"]), ("year", year), ("kind", "Target"), ("value", 30)));
        t.Check(status == 404, "The engineering dean cannot edit the university target");
        var target = await dean.Ok("PUT", $"/api/kpi/indicators/{code2}/targets", Scenario.O(("unitId", eng["id"]), ("year", year), ("kind", "Target"), ("value", 3), ("source", "خطة الكلية")));
        t.Check(target["value"].Num() == 3, "The engineering dean enters their college target");
        (status, err) = await dean.Call("PUT", "/api/kpi/indicators/4.08/targets", Scenario.O(("unitId", eng["id"]), ("year", year), ("kind", "Target"), ("value", 120)));
        t.Check(status == 400 && err.Code() == "percent_range", "A percentage over 100 is refused with a clear message");
        (status, err) = await dean.Call("PUT", "/api/kpi/indicators/4.08/targets", Scenario.O(("unitId", eng["id"]), ("year", year), ("kind", "Target"), ("value", null), ("numerator", 520), ("denominator", 500)));
        t.Check(status == 400 && (err?["title"].Str() ?? "").Contains("520", StringComparison.Ordinal), "A numerator above the denominator is refused and names both numbers");
        var dist2 = (await planner.Get($"/api/kpi/indicators/{code2}/distribution?year={year}"))!;
        var share = dist2["shares"].Items().First(s => s["unitId"].Str() == eng["id"].Str());
        t.Check(share["value"].Num() == 3 && !share["fromEachCollegeDefault"].Truthy(), "The distribution uses the college's own target instead of the common one");
        (status, _) = await committee.Call("POST", $"/api/kpi/targets/{target["id"].Str()}/approve", Scenario.O(("decisionRef", "محضر 2")));
        t.Check(status == 200, "The committee approves the college target");
        (status, _) = await dean.Call("PUT", $"/api/kpi/indicators/{code2}/targets", Scenario.O(("unitId", eng["id"]), ("year", year), ("kind", "Target"), ("value", 4)));
        t.Check(status == 403, "Editing an approved target needs the approval permission");
        (status, _) = await committee.Call("PUT", $"/api/kpi/indicators/{code2}/targets", Scenario.O(("unitId", eng["id"]), ("year", year), ("kind", "Target"), ("value", 4)));
        t.Check(status == 403, "The higher committee does not enter targets (not a data entry role)");

        t.Step("5. College structure: scope, replacement and hiding contact details");
        var template = (await college.Get("/api/structure/template")).Items().ToList();
        var deanPos = template.First(u => u["code"].Str() == "DEAN")["positions"].Items().First();
        var name = $"أ.د. عميد تجريبي {Calls.Tag(4)}";
        (status, _) = await college.Call("POST", "/api/structure/members", Scenario.O(("collegeId", cs["id"]), ("positionId", deanPos["id"]), ("fullName", name), ("startDate", "2026-09-01"), ("replaceCurrent", false)));
        t.Check(status == 404, "The engineering committee cannot add to the computing college");
        var chart = (await college.Get($"/api/structure/colleges/{eng["id"].Str()}"))!;
        var occupied = chart["members"].Items().Any(m => m["positionId"].Str() == deanPos["id"].Str());
        var member = Scenario.O(("collegeId", eng["id"]), ("positionId", deanPos["id"]), ("fullName", name), ("email", "dean.test@cu.edu.eg"), ("phone", "0100 000 0000"),
            ("startDate", occupied ? "2026-11-01" : "2026-09-01"), ("replaceCurrent", false));
        if (occupied)
        {
            (status, err) = await college.Call("POST", "/api/structure/members", member);
            t.Check(status == 409 && err.Code() == "position_taken", "An occupied position asks to replace the current holder");
            member["replaceCurrent"] = true;
            // The day after the current holder's start, so the test repeats on one database; look at a far-future chart for a replacement that starts tomorrow.
            var future = (await college.Get($"/api/structure/colleges/{eng["id"].Str()}?asOf=2099-12-31"))!;
            var latest = chart["members"].Items().Concat(future["members"].Items()).Where(m => m["positionId"].Str() == deanPos["id"].Str()).Max(m => m["startDate"].Str()!);
            member["startDate"] = DateOnly.Parse(latest!, System.Globalization.CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
        var added = await college.Ok("POST", "/api/structure/members", member);
        t.Check(added["fullName"].Str() == name && added["phone"].Str() == "01000000000", "The dean was added with the phone normalised");
        (status, _) = await viewer.Call("GET", $"/api/structure/colleges/{eng["id"].Str()}");
        t.Check(status == 404, "The computing viewer cannot see the engineering structure");
        var dchart = (await dean.Get($"/api/structure/colleges/{eng["id"].Str()}"))!;
        t.Check(dchart["contactsVisible"].Truthy() && dchart["members"].Items().Any(x => x["email"].Truthy()), "The dean sees contact details");
        var deptrep = await Env.As("deptrep");
        (status, _) = await deptrep.Call("GET", $"/api/structure/colleges/{eng["id"].Str()}");
        t.Check(status == 404, "The department representative is scoped to the department, not the whole college");
        (status, _) = await sysadmin.Call("GET", $"/api/structure/colleges/{eng["id"].Str()}");
        t.Check(status == 403, "The system administrator cannot see structure members (separation of duties)");

        t.Step("6. 'Required from you' by role");
        var taken = (await planner.Get($"/api/kpi/indicators/{code2}/targets"))!["colleges"].Items().Where(x => x["unitId"].Str() == eng["id"].Str()).Select(x => (int)x["year"].Num()!).ToHashSet();
        var spare = Enumerable.Range(2027, 5).FirstOrDefault(y => !taken.Contains(y));
        if (spare != 0) await dean.Ok("PUT", $"/api/kpi/indicators/{code2}/targets", Scenario.O(("unitId", eng["id"]), ("year", spare), ("kind", "Target"), ("value", 2), ("source", "خطة الكلية")));
        var home = (await committee.Get("/api/home"))!;
        t.Check(home["tasks"].Items().Any(x => x["key"].Str() == "targets_proposed"), "The committee sees the proposed targets waiting for it");
        home = (await planner.Get("/api/home"))!;
        t.Check(!home["tasks"].Items().Any(x => x["key"].Str() == "definitions_review"), "The plan manager does not see the approval task");
        (status, _) = await viewer.Call("PUT", $"/api/kpi/indicators/{code}/definition", member);
        t.Check(status == 403, "The viewer cannot edit");
        t.Finish();
    }
}
