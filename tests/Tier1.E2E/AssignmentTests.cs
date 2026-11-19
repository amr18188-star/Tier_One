using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// Assigning indicators to units, confining each officer to their unit, and units that see the whole university.
/// Customises Commerce / QAC / STU, opens a trial cycle by the assignment, then restores everything and closes the trial.
/// </summary>
public sealed class AssignmentTests(ITestOutputHelper output)
{
    [E2EFact]
    public async Task Assignment_scope_and_plan_document()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var admin = await Env.As("sa1");
        var entryCom = await Env.As("entry.com");
        var central = await Env.As("entry.central");
        var units = await Env.UnitsAsync(planner);
        JsonNode com = units["COM"], eng = units["ENG"], qac = units["QAC"], stu = units["STU"], acc = units["COM-ACC"];
        string Id(JsonNode u) => u["id"].Str()!;

        // A run stopped half-way leaves units customised; start from the default like the cleanup at the end does.
        foreach (var u in new[] { com, qac, stu }) await planner.Ok("PUT", $"/api/org/units/{Id(u)}/indicators", Scenario.O(("custom", false), ("indicatorIds", new JsonArray())));

        t.Step("1. Unit indicators: the default, then customising");
        var d = (await planner.Get($"/api/org/units/{Id(com)}/indicators"))!;
        var items = d["standards"].Items().SelectMany(s => s["indicators"].Items()).ToList();
        var collegeLevel = items.Where(i => i["level"].Str() == "College").ToList();
        var deptLevel = items.Where(i => i["level"].Str() == "Department").ToList();
        t.Check(!d["custom"].Truthy() && collegeLevel.All(i => i["selected"].Truthy()), $"Commerce is not customised: it enters every college indicator ({collegeLevel.Count})");
        t.Check(deptLevel.Count >= 1, "The college list includes its departments' indicators");
        var chosen = new JsonArray((JsonNode)Id(collegeLevel[0]), Id(collegeLevel[1]), Id(deptLevel[0]));
        d = await planner.Ok("PUT", $"/api/org/units/{Id(com)}/indicators", Scenario.O(("custom", true), ("indicatorIds", chosen)));
        var picked = d["standards"].Items().SelectMany(s => s["indicators"].Items()).Where(i => i["selected"].Truthy()).Select(i => i["id"].Str()).ToHashSet();
        t.Check(d["custom"].Truthy() && picked.SetEquals(chosen.Select(c => c!.GetValue<string>())), "The Commerce list saved: 3 indicators");
        var (status, err) = await entryCom.Call("PUT", $"/api/org/units/{Id(com)}/indicators", Scenario.O(("custom", false), ("indicatorIds", new JsonArray())));
        t.Check(status == 403, "A data entry user cannot change assignments");
        var dacc = (await planner.Get($"/api/org/units/{Id(acc)}/indicators"))!;
        t.Check(dacc["followsUnit"].Str() == com["nameAr"].Str() && dacc["standards"].Items().SelectMany(s => s["indicators"].Items()).Where(i => i["selected"].Truthy()).Select(i => i["id"].Str()).SequenceEqual([Id(deptLevel[0])]),
            "The accounting department follows its college's list");

        t.Step("2. A university indicator is entered by one unit");
        var dq = (await planner.Get($"/api/org/units/{Id(qac)}/indicators"))!;
        var uniItems = dq["standards"].Items().SelectMany(s => s["indicators"].Items()).ToList();
        t.Check(uniItems.Count > 0 && uniItems.All(i => i["level"].Str() == "University"), $"A central unit is assigned university indicators only ({uniItems.Count})");
        var target = uniItems.First(i => !i["takenBy"].Truthy());
        await planner.Ok("PUT", $"/api/org/units/{Id(qac)}/indicators", Scenario.O(("custom", true), ("indicatorIds", new JsonArray((JsonNode)Id(target)))));
        (status, err) = await planner.Call("PUT", $"/api/org/units/{Id(stu)}/indicators", Scenario.O(("custom", true), ("indicatorIds", new JsonArray((JsonNode)Id(target)))));
        t.Check(status == 409 && err.Code() == "already_assigned", $"{target["code"].Str()} cannot be assigned to a second unit");
        (status, err) = await planner.Call("PUT", $"/api/org/units/{Id(stu)}/indicators", Scenario.O(("custom", true), ("indicatorIds", new JsonArray((JsonNode)Id(collegeLevel[0])))));
        t.Check(status == 400 && err.Code() == "not_assignable", "A central unit cannot be assigned a college indicator");
        var dst = (await planner.Get($"/api/org/units/{Id(stu)}/indicators"))!;
        t.Check(dst["standards"].Items().SelectMany(s => s["indicators"].Items()).First(i => i["id"].Str() == Id(target))["takenBy"].Str() == qac["nameAr"].Str(), "The other unit sees it is assigned to QAC");
        var listed = (await Env.UnitsAsync(planner));
        t.Check(listed["COM"]["indicatorCount"].Num() == 3 && listed["QAC"]["indicatorCount"].Num() == 1 && listed["ENG"]["indicatorCount"].Num() > 3,
            $"Indicator counts in the unit list: Commerce 3, QAC 1, Engineering {listed["ENG"]["indicatorCount"].Num()}");

        t.Step("3. A trial cycle opens by the assignment");
        var label = $"تكليف-{Calls.Tag()}";
        var cycle = await planner.Ok("POST", "/api/cycles", Scenario.O(("name", $"بروفة {label}"), ("periodLabel", label), ("targetYear", 2027), ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", true), ("version", null)));
        cycle = await planner.Ok("POST", $"/api/cycles/{cycle["id"].Str()}/open");
        var cycleId = cycle["id"].Str();
        var forms = (await planner.Get($"/api/forms?cycleId={cycleId}")).Items().ToDictionary(f => f["unitId"].Str()!);
        async Task<HashSet<string?>> Codes(JsonNode unit)
        {
            var f = (await planner.Get($"/api/forms/{forms[Id(unit)]["id"].Str()}"))!;
            return f.ItemsOf().Select(i => i["indicatorId"].Str()).ToHashSet();
        }
        var comCodes = await Codes(com);
        t.Check(comCodes.IsSupersetOf([Id(collegeLevel[0]), Id(collegeLevel[1])]) && comCodes.IsSubsetOf([Id(collegeLevel[0]), Id(collegeLevel[1]), Id(deptLevel[0])]), "The Commerce form has its assigned indicators only");
        t.Check((await Codes(acc)).SetEquals([Id(deptLevel[0])]), "The accounting department form has only its assigned department indicator");
        t.Check(collegeLevel.Select(i => i["id"].Str()).ToHashSet().IsSubsetOf(await Codes(eng)), "The Engineering form has every college indicator");
        t.Check((await Codes(qac)).SetEquals([Id(target)]), $"The QAC form has only the university indicator {target["code"].Str()}");
        var uni = units.Values.First(u => u["kind"].Str() == "University");
        t.Check(!forms.ContainsKey(Id(uni)) || !(await Codes(uni)).Contains(Id(target)), "The indicator left the university form");

        t.Step("4. An officer sees only their unit");
        var mine = (await entryCom.Get($"/api/forms?cycleId={cycleId}")).Items().ToList();
        var allowed = units.Values.Where(u => u["path"].Str()!.StartsWith(com["path"].Str()!, StringComparison.Ordinal)).Select(Id).ToHashSet();
        t.Check(mine.Count > 0 && mine.All(f => allowed.Contains(f["unitId"].Str()!)), $"The Commerce entry user sees {mine.Count} forms, all Commerce and its departments");
        (status, _) = await entryCom.Call("GET", $"/api/forms/{forms[Id(eng)]["id"].Str()}");
        t.Check(status == 404, "The Engineering form does not exist for them (404)");
        var byId = units.Values.ToDictionary(Id);
        var seen = (await central.Get($"/api/forms?cycleId={cycleId}")).Items().ToList();
        // On the official structure the ICT centre (DIG) is created with whole-university view, so the central entry user always sees every form.
        var alreadyWhole = new[] { "QAC", "STU", "DIG", "INTL" }.Any(c => units[c]["viewsWholeUniversity"].Truthy());
        if (!alreadyWhole) t.Check(seen.All(f => byId[f["unitId"].Str()!]["code"].Str() is "QAC" or "STU" or "DIG" or "INTL"), "The central entry user sees their units' forms only");
        var directory = (await entryCom.Get("/api/org/units")).Items().ToList();
        t.Check(directory.Count > 0 && directory.All(u => u["path"].Str()!.StartsWith(com["path"].Str()!, StringComparison.Ordinal)), $"Their unit directory: their college and departments only ({directory.Count} units)");
        (status, _) = await entryCom.Call("GET", $"/api/org/units/{Id(eng)}/indicators");
        t.Check(status == 404, "They cannot open another college's indicators");
        (status, _) = await entryCom.Call("GET", "/api/scorecard");
        t.Check(status == 403 && (await planner.Call("GET", "/api/scorecard")).Status == 200, "The university scorecard is for whole-university scope only");
        var row = (await entryCom.Get("/api/cycles")).Items().First(c => c["id"].Str() == cycleId);
        t.Check(row["forms"].Num() == mine.Count, $"Their cycle counts are their unit's forms ({row["forms"].Num()}), not the university's ({forms.Count})");

        t.Step("5. A unit that sees the whole university");
        async Task<JsonObject> Edit(bool views) => Scenario.O(("nameAr", qac["nameAr"]), ("nameEn", qac["nameEn"]), ("sortOrder", qac["sortOrder"]), ("effectiveOn", null), ("reason", null), ("version", (await admin.Get("/api/org/units")).Items().Single(u => u["id"].Str() == Id(qac))["version"]), ("viewsWholeUniversity", views));
        // On the official structure QAC (SPU) already sees the whole university; start narrow so widening is a real change, and put it back after.
        var qacWasWide = qac["viewsWholeUniversity"].Truthy();
        if (qacWasWide) await admin.Ok("PUT", $"/api/org/units/{Id(qac)}", await Edit(false));
        (status, err) = await planner.Call("PUT", $"/api/org/units/{Id(qac)}", await Edit(true));
        t.Check(status == 403 && err.Code() == "grants_required", "The plan manager cannot widen a unit's view");
        var saved = await admin.Ok("PUT", $"/api/org/units/{Id(qac)}", await Edit(true));
        t.Check(saved["viewsWholeUniversity"].Truthy(), "The super admin (who manages permissions) made QAC see the whole university");
        seen = (await central.Get($"/api/forms?cycleId={cycleId}")).Items().ToList();
        t.Check(seen.Count == forms.Count, $"The QAC entry user sees every form ({seen.Count} of {forms.Count})");
        t.Check((await central.Get("/api/org/units")).Items().Count() == units.Count, "And the whole unit directory");
        var f1 = (await central.Get($"/api/forms/{forms[Id(eng)]["id"].Str()}"))!;
        t.Check(!f1["actions"]!["canEdit"].Truthy(), "And reads the Engineering form without editing it");
        f1 = (await central.Get($"/api/forms/{forms[Id(qac)]["id"].Str()}"))!;
        t.Check(f1["actions"]!["canEdit"].Truthy(), "And edits their own unit's form");
        await admin.Ok("PUT", $"/api/org/units/{Id(qac)}", await Edit(false));
        seen = (await central.Get($"/api/forms?cycleId={cycleId}")).Items().ToList();
        if (!alreadyWhole) t.Check(seen.Count < forms.Count, "Removing the view returns them to their units at once");
        if (qacWasWide) await admin.Ok("PUT", $"/api/org/units/{Id(qac)}", await Edit(true));

        t.Step("6. Restore what changed");
        foreach (var u in new[] { com, qac, stu }) await planner.Ok("PUT", $"/api/org/units/{Id(u)}/indicators", Scenario.O(("custom", false), ("indicatorIds", new JsonArray())));
        await planner.Ok("POST", $"/api/cycles/{cycleId}/close", Scenario.O(("note", "انتهى اختبار التكليف")));
        var back = await Env.UnitsAsync(planner);
        t.Check(!back["COM"]["customIndicators"].Truthy() && back["QAC"]["indicatorCount"].Num() == 0, "The units are back to the default and the trial is closed");
        t.Check(back["QAC"]["viewsWholeUniversity"].Truthy() == qacWasWide, "QAC keeps the whole-university view it started with");

        t.Step("7. The strategic plan document: the plan manager uploads, everyone reads");
        (status, _) = await entryCom.Upload("/api/strategy/documents", "plan.pdf", Env.Pdf);
        t.Check(status == 403, "A data entry user cannot upload the plan document");
        (status, err) = await planner.Upload("/api/strategy/documents", "plan.docx", [.. "PK\x03\x04rest"u8]);
        t.Check(status == 400 && err.Code() == "pdf_only", "The document must be a PDF");
        (status, var doc) = await planner.Upload("/api/strategy/documents", "plan.pdf", Env.Pdf, ("description", "خطة اختبار"));
        t.Check(status == 200 && doc!["description"].Str() == "خطة اختبار", "The plan manager uploaded a version");
        var docs = (await entryCom.Get("/api/strategy/documents")).Items().ToList();
        t.Check(docs.Count > 0 && docs[0]["document"]!["id"].Str() == doc!["id"].Str(), "The Commerce entry user sees the latest version");
        var view = await entryCom.Download($"/api/strategy/documents/{doc!["id"].Str()}/view");
        t.Check(view.Body.AsSpan().StartsWith("%PDF"u8) && view.ContentType.StartsWith("application/pdf", StringComparison.Ordinal) && view.Headers.GetValueOrDefault("X-Frame-Options") == "SAMEORIGIN",
            "It opens inside the platform (a PDF allows a frame from the system itself)");
        await planner.Ok("POST", $"/api/documents/{doc["id"].Str()}/void", Scenario.O(("note", "انتهى الاختبار")));
        t.Check((await entryCom.Get("/api/strategy/documents")).Items().All(x => x["document"]!["id"].Str() != doc["id"].Str()), "A voided version disappears for account holders");
        (status, _) = await entryCom.Call("GET", $"/api/strategy/documents/{doc["id"].Str()}/view");
        t.Check(status == 404, "And does not open for them");
        t.Finish();
    }
}
