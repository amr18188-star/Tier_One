using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Tier1.Infrastructure.Files;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// Data collection end to end: a trial cycle, validated entry, evidence, the return-with-notes and approval path with separation of duties,
/// Excel import (preview, then all-or-nothing apply), results, a new indicator, closing and the notification mail.
/// </summary>
public sealed class CollectionTests(ITestOutputHelper output)
{
    // The test host runs from an output folder, so find the repository from this source file's own location.
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(here)!); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private static int MailCount()
    {
        var root = RepoRoot();
        return new[] { Path.Combine(root, "data", "mail"), Path.Combine(root, "src", "Tier1.Api", "data", "mail") }
            .Where(Directory.Exists).Sum(d => Directory.GetFiles(d, "*.eml").Length);
    }

    [E2EFact]
    public async Task Cycle_entry_approval_import_results_and_close()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var entry = await Env.As("entry");
        var college = await Env.As("college");
        var dean = await Env.As("dean");
        var quality = await Env.As("quality");
        var deptrep = await Env.As("deptrep");
        var viewer = await Env.As("viewer");
        var units = await Env.UnitsAsync(planner);
        var eng = units["ENG"];
        var civ = units["ENG-CIV"];
        var mailBefore = MailCount();

        t.Step("1. Create a trial cycle and open it");
        var label = $"تجربة-{Calls.Tag()}";
        var body = Scenario.O(("name", $"الدورة التجريبية {label}"), ("periodLabel", label), ("targetYear", 2027), ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", true), ("version", null));
        var (status, err) = await viewer.Call("POST", "/api/cycles", body);
        t.Check(status == 403, "The viewer cannot create a cycle");
        (status, err) = await planner.Call("POST", "/api/cycles", Scenario.With(body, ("dueOn", Calls.Today(-1))));
        t.Check(status == 400 && err.Code() == "dates", "A deadline before the opening is refused");
        var cycle = await planner.Ok("POST", "/api/cycles", body);
        var cycleId = cycle["id"].Str();
        t.Check(cycle["status"].Str() == "Draft", "The cycle was created as a draft");
        t.Check((await entry.Get("/api/cycles")).Items().All(c => c["id"].Str() != cycleId), "A draft cycle is hidden from data entry users");
        var preview = (await planner.Get($"/api/cycles/{cycleId}/preview"))!;
        t.Check(preview["indicators"].Num() > 0 && preview["forms"].Num() > 0, $"Preview: {preview["indicators"].Num()} indicators in {preview["forms"].Num()} forms");
        cycle = await planner.Ok("POST", $"/api/cycles/{cycleId}/open");
        t.Check(cycle["status"].Str() == "Open", "The cycle opened and the forms were created");
        (status, _) = await planner.Call("POST", $"/api/cycles/{cycleId}/open");
        t.Check(status is 400 or 409 or 422, "A cycle does not open twice");

        t.Step("2. The data entry user's forms and validated entry");
        var mine = (await entry.Get($"/api/forms?cycleId={cycleId}&awaitingMe=true")).Items().ToList();
        t.Check(mine.Any(f => f["unitId"].Str() == eng["id"].Str()), "The Engineering form is in 'required from me'");
        t.Check(mine.All(f => f["unitName"].Truthy()) && !mine.Any(f => f["unitId"].Str() != eng["id"].Str() && f["parentName"].Str() is { } p && p != eng["nameAr"].Str()), "The Engineering entry user sees no other college's forms");
        var formRow = mine.First(f => f["unitId"].Str() == eng["id"].Str());
        var computingId = units["CS"]["id"].Str();
        var computingForm = (await planner.Get($"/api/forms?cycleId={cycleId}")).Items()
            .Single(row => row["unitId"].Str() == computingId);
        var computingFormId = computingForm["id"].Str();
        var computingEntry = await Env.As("entry.cs");
        (status, _) = await computingEntry.Call("GET", $"/api/forms/{computingFormId}");
        t.Check(status == 200, "The Computing entry user can open their college form");
        var deanForms = (await dean.Get($"/api/forms?cycleId={cycleId}")).Items();
        t.Check(deanForms.Any(row => row["id"].Str() == formRow["id"].Str())
            && deanForms.All(row => row["id"].Str() != computingFormId), "The Engineering dean's form list excludes the Computing form");
        (status, _) = await dean.Call("GET", $"/api/forms/{computingFormId}");
        t.Check(status == 404, "The Engineering dean cannot open the Computing form by direct link");
        (status, _) = await viewer.Call("GET", $"/api/forms/{formRow["id"].Str()}");
        t.Check(status == 404, "The computing viewer cannot see the Engineering form (404)");
        var form = (await entry.Get($"/api/forms/{formRow["id"].Str()}"))!;
        var items = form.ItemsOf().ToList();
        t.Check(form["actions"]!["canEdit"].Truthy() && !form["actions"]!["canReview"].Truthy(), "The entry user edits but does not review");
        t.Check(form["path"]![0]!["state"].Str() == "current", "The approval path starts at the entry step");

        var pct = items.FirstOrDefault(i => i["unit"].Str()?.Trim() == "%" && !i["usesRatio"].Truthy());
        if (pct is not null)
        {
            (status, _) = await entry.Call("PUT", $"/api/forms/{form["id"].Str()}/values", Scenario.With(Calls.InputFor(pct), ("value", 150)));
            t.Check(status == 400, "A percentage above 100% is refused");
        }
        var ratio = items.FirstOrDefault(i => i["usesRatio"].Truthy());
        if (ratio is not null)
        {
            (status, _) = await entry.Call("PUT", $"/api/forms/{form["id"].Str()}/values", Scenario.With(Calls.InputFor(ratio), ("numerator", 60), ("denominator", 50)));
            t.Check(status == 400, "A numerator above the denominator is refused");
            var saved = await entry.Ok("PUT", $"/api/forms/{form["id"].Str()}/values", Calls.InputFor(ratio));
            t.Check(Math.Abs((double)saved["value"]!["value"].Num()! - 90) < 0.001, "The ratio is computed on the server from numerator and denominator (45/50 = 90%)");
        }
        (status, _) = await entry.Call("PUT", $"/api/forms/{form["id"].Str()}/values", Scenario.With(Calls.InputFor(items[^1]), ("kind", "NotAvailable"), ("reason", "لا")));
        t.Check(status == 400, "'Not available' without a sufficient reason is refused");
        (status, err) = await entry.Call("POST", $"/api/forms/{form["id"].Str()}/submit", await entry.Versioned($"/api/forms/{form["id"].Str()}"));
        t.Check(status is 400 or 422 && err.Code().Length > 0, "Submitting is refused while the form is incomplete");

        var evItem = items.FirstOrDefault(i => i["evidenceRequired"].Truthy()) ?? items[0];
        var evidencePath = $"/api/forms/{form["id"].Str()}/values/{evItem["indicatorId"].Str()}/evidence";
        (status, _) = await entry.Upload(evidencePath, "fake.pdf", "hello world"u8.ToArray());
        t.Check(status == 400, "A file that does not match the PDF signature is refused");

        form = await entry.Fill(form);
        t.Check(form["completed"].Num() == form["required"].Num(), $"The form is complete: {form["completed"].Num()}/{form["required"].Num()}");

        var evidence = Env.UniquePdf();
        (status, var doc) = await entry.Upload(evidencePath, "كشف الطلاب.pdf", evidence, ("description", "كشف"));
        t.Check(status == 200 && doc!["contentType"].Str() == "application/pdf", "A valid PDF evidence uploaded");
        var dl = await entry.Download($"/api/documents/{doc!["id"].Str()}");
        t.Check(dl.Status == 200 && dl.Body.SequenceEqual(evidence), "The evidence downloads as uploaded");
        t.Check((await viewer.Download($"/api/documents/{doc["id"].Str()}")).Status is 403 or 404, "A viewer of another college cannot download the evidence");
        (status, _) = await entry.Call("POST", $"/api/documents/{doc["id"].Str()}/void", Scenario.O(("note", "")));
        t.Check(status == 400, "Voiding asks for a reason");
        var voided = await entry.Ok("POST", $"/api/documents/{doc["id"].Str()}/void", Scenario.O(("note", "رفع بالخطأ")));
        t.Check(voided["voided"].Truthy(), "The evidence is voided and stays in the log");

        (status, var sdoc) = await college.Upload($"/api/structure/colleges/{eng["id"].Str()}/documents", "هيكل الهندسة المعتمد.pdf", Env.UniquePdf());
        t.Check(status == 200, "The college committee uploads the approved structure");
        var docs = (await college.Get($"/api/structure/colleges/{eng["id"].Str()}/documents")).Items();
        t.Check(docs.Any(x => x["id"].Str() == sdoc!["id"].Str()), "The document is in the college's list");
        (status, _) = await viewer.Call("GET", $"/api/structure/colleges/{eng["id"].Str()}/documents");
        t.Check(status == 404, "Someone outside the college does not see its documents");
        t.Check(!form["blocking"].Truthy(), "Nothing blocks submission");
        form = await entry.Ok("POST", $"/api/forms/{form["id"].Str()}/submit", await entry.Versioned($"/api/forms/{form["id"].Str()}"));
        t.Check(form["step"].Str() == "CollegeCommittee" && form["status"].Str() == "InReview", "The form was sent to the college committee");
        (status, _) = await entry.Call("PUT", $"/api/forms/{form["id"].Str()}/values", Calls.InputFor(items[0]));
        t.Check(status == 409, "A submitted form is locked for editing");

        (status, _) = await entry.Call("POST", $"/api/forms/{form["id"].Str()}/extend", Scenario.O(("dueOn", Calls.Today(20)), ("reason", "ظروف الكلية")));
        t.Check(status == 403, "The entry user cannot extend their deadline");
        var ext = await planner.Ok("POST", $"/api/forms/{form["id"].Str()}/extend", Scenario.O(("dueOn", Calls.Today(20)), ("reason", "قرار اللجنة بتمديد الهندسة")));
        t.Check(ext["dueOn"].Str() == Calls.Today(20), "Engineering alone was extended by a recorded decision");
        var othersDue = (await planner.Get($"/api/forms?cycleId={cycleId}")).Items().Where(f => f["id"].Str() != form["id"].Str()).Select(f => f["dueOn"].Str()).ToHashSet();
        t.Check(othersDue.SetEquals([cycle["dueOn"].Str()]), "The other units' deadlines did not change");

        t.Step("3. Return with notes, then the approval path with separation of duties");
        (status, _) = await entry.Call("POST", $"/api/forms/{form["id"].Str()}/approve", await entry.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", null))));
        t.Check(status == 403, "The entry user cannot approve");
        (status, _) = await quality.Call("POST", $"/api/forms/{form["id"].Str()}/approve", await quality.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", null))));
        t.Check(status == 403, "Central quality cannot approve before its turn");
        t.Check((await college.Get($"/api/forms?cycleId={cycleId}&awaitingMe=true")).Items().Any(f => f["id"].Str() == form["id"].Str()), "The form is in 'required from me' for the college committee");
        (status, _) = await college.Call("POST", $"/api/forms/{form["id"].Str()}/return", await college.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", ""), ("items", new JsonArray()))));
        t.Check(status == 400, "Returning asks for a note");
        var first = items[0];
        form = await college.Ok("POST", $"/api/forms/{form["id"].Str()}/return", await college.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", "راجعوا مصدر المؤشر الأول"),
            ("items", new JsonArray(Scenario.O(("indicatorId", first["indicatorId"]), ("note", "الرقم لا يطابق كشف شؤون الطلاب")))))));
        t.Check(form["status"].Str() == "Returned" && form["step"].Str() == "Entry", "The form was returned for correction");
        form = (await entry.Get($"/api/forms/{form["id"].Str()}"))!;
        var flagged = form.ItemsOf().First(i => i["indicatorId"].Str() == first["indicatorId"].Str());
        t.Check(flagged["value"]!["reviewNote"].Str() == "الرقم لا يطابق كشف شؤون الطلاب", "The reviewer's note shows on the indicator");
        var fix = Calls.InputFor(flagged);
        fix["explanation"] = "صحح من الكشف المعتمد";
        await entry.Ok("PUT", $"/api/forms/{form["id"].Str()}/values", fix);
        form = await entry.Ok("POST", $"/api/forms/{form["id"].Str()}/submit", await entry.Versioned($"/api/forms/{form["id"].Str()}"));
        t.Check(form["step"].Str() == "CollegeCommittee", "Resubmitted after the correction");

        form = await college.Ok("POST", $"/api/forms/{form["id"].Str()}/approve", await college.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", "مراجع"))));
        t.Check(form["step"].Str() == "Dean", "The college committee approved and it moved to the dean");
        form = await dean.Ok("POST", $"/api/forms/{form["id"].Str()}/approve", await dean.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", null))));
        t.Check(form["step"].Str() == "Quality", "The dean approved and it moved to central quality");
        form = await quality.Ok("POST", $"/api/forms/{form["id"].Str()}/approve", await quality.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", null))));
        t.Check(form["status"].Str() == "Approved" && form["step"].Str() == "Done", "Final approval by central quality");
        t.Check(form["events"].Items().Count(e => e["action"].Str() == "Approved") == 3, "The form's log keeps every approval step");
        (status, _) = await entry.Call("PUT", $"/api/forms/{form["id"].Str()}/values", Calls.InputFor(items[0]));
        t.Check(status == 409, "An approved form is locked");

        t.Step("4. A department form: the representative who entered does not review (SOD-C1)");
        var civRow = (await deptrep.Get($"/api/forms?cycleId={cycleId}")).Items().FirstOrDefault(f => f["unitId"].Str() == civ["id"].Str());
        if (civRow is null) t.Check(true, "No department-level indicators in this cycle (no department form)");
        else
        {
            var cf = (await deptrep.Get($"/api/forms/{civRow["id"].Str()}"))!;
            cf = await deptrep.Fill(cf);
            cf = await deptrep.Ok("POST", $"/api/forms/{cf["id"].Str()}/submit", await deptrep.Versioned($"/api/forms/{cf["id"].Str()}"));
            (status, err) = await deptrep.Call("POST", $"/api/forms/{cf["id"].Str()}/approve", await deptrep.Versioned($"/api/forms/{cf["id"].Str()}", Scenario.O(("note", null))));
            t.Check(status == 403 && err.Code().StartsWith("sod_", StringComparison.Ordinal), $"Whoever entered does not review their own form ({err.Code()})");

            var departmentPath = $"/api/forms/{cf["id"].Str()}";
            var committeeForm = (await college.Get(departmentPath))!;
            t.Check(committeeForm["actions"]!["directDepartmentApproval"].Truthy(), "The college committee can approve while the department review is pending");
            t.Check((await college.Get($"/api/forms?cycleId={cycleId}&awaitingMe=true")).Items().Any(f => f["id"].Str() == cf["id"].Str()),
                "The department form appears in the committee's queue");
            var version = committeeForm["version"];
            // D2: the skip is an explicit choice with a written reason; a plain approve does not turn into a direct one.
            t.Check(!committeeForm["actions"]!["canApprove"].Truthy() && committeeForm["actions"]!["directStepName"].Str() is { Length: > 0 },
                "The committee is offered direct approval only (it does not hold the department review)");
            (status, err) = await college.Call("POST", departmentPath + "/approve", Scenario.O(("note", null), ("version", version)));
            t.Check(status == 409 && err.Code() == "direct_only", $"A plain approve does not silently skip a step ({status} {err.Code()})");
            (status, err) = await college.Call("POST", departmentPath + "/approve", Scenario.O(("note", null), ("version", version), ("direct", true)));
            t.Check(status == 400 && err.Code() == "direct_reason_required", $"A direct approval needs a reason ({status} {err.Code()})");
            const string skipReason = "ممثل القسم في إجازة رسمية";
            cf = await college.Ok("POST", departmentPath + "/approve", Scenario.O(("note", skipReason), ("version", version), ("direct", true)));
            t.Check(cf["step"].Str() == "Dean" && cf["path"].Items().Any(p => p["step"].Str() == "DepartmentReview" && p["state"].Str() == "skipped"),
                "Direct approval skips department review and advances to the dean");
            t.Check(cf["events"].Items().Any(e => e["action"].Str() == "Approved" && e["fromStep"].Str() == "DepartmentReview" && e["toStep"].Str() == "Dean"
                                                  && e["note"].Str() == skipReason),
                "The form history records the direct approval and its reason");
            (status, _) = await deptrep.Call("POST", departmentPath + "/approve", Scenario.O(("note", null), ("version", version)));
            t.Check(status == 409, "An approval from a stale form version cannot move the form again");
        }

        if (units.TryGetValue("ENG-ELE", out var ele)
            && (await entry.Get($"/api/forms?cycleId={cycleId}")).Items().FirstOrDefault(f => f["unitId"].Str() == ele["id"].Str()) is { } eleRow)
        {
            var path = $"/api/forms/{eleRow["id"].Str()}";
            await entry.Fill((await entry.Get(path))!);
            var ef = await entry.Ok("POST", path + "/submit", await entry.Versioned(path));
            var deanForm = (await dean.Get(path))!;
            t.Check(deanForm["actions"]!["directDeanApproval"].Truthy(), "The dean can approve a department form directly");
            // D2: whoever may pass the form on may also send it back.
            var returned = await dean.Ok("POST", path + "/return", await dean.Versioned(path, Scenario.O(("note", "أكمل أدلة المؤشرات قبل الاعتماد"), ("items", new JsonArray()))));
            t.Check(returned["status"].Str() == "Returned", "A direct-only approver (the dean) can return the form for correction");
            ef = await entry.Ok("POST", path + "/submit", await entry.Versioned(path));
            ef = await dean.Ok("POST", path + "/approve", Scenario.O(("version", ef["version"]), ("direct", true), ("note", "اعتماد مباشر لضيق موعد الدورة")));
            t.Check(ef["step"].Str() == "Quality"
                && ef["path"].Items().Count(p => p["state"].Str() == "skipped") == 2,
                "Direct dean approval skips department and college review and advances to central quality");
        }

        t.Step("5. Reopening by request and higher-committee decision (D4), then Excel import");
        var committee = await Env.As("committee");
        var formPath = $"/api/forms/{form["id"].Str()}";
        Dictionary<string, decimal?> EngActuals(JsonNode r) =>
            r["indicators"].Items().Where(i => i["actual"].Num() is not null).ToDictionary(i => i["code"].Str()!, i => i["actual"].Num());
        var officialBefore = EngActuals((await planner.Get($"/api/results?cycleId={cycleId}&unitId={eng["id"].Str()}"))!);
        (status, _) = await entry.Call("POST", formPath + "/reopen", await entry.Versioned(formPath, Scenario.O(("note", "سبب كاف"))));
        t.Check(status == 403, "Requesting a reopen is a special permission only");
        var requested = await planner.Ok("POST", formPath + "/reopen", await planner.Versioned(formPath, Scenario.O(("note", "تصحيح بعد مراجعة اللجنة العليا"))));
        t.Check(requested["status"].Str() == "Approved" && requested["reopen"]!["status"].Str() == "Pending" && !requested["actions"]!["canReopen"].Truthy(),
            "The request leaves the form approved and locked until the committee decides");
        (status, err) = await planner.Call("POST", formPath + "/reopen", await planner.Versioned(formPath, Scenario.O(("note", "طلب ثان للنموذج نفسه"))));
        t.Check(status == 409 && err.Code() == "reopen_pending", "Only one pending request per form");
        (status, _) = await planner.Call("POST", formPath + "/reopen/approve", Scenario.O(("decisionRef", "محضر 7")));
        t.Check(status == 403, "The plan manager who requested cannot approve (higher committee only)");
        t.Check((await committee.Get(formPath))!["actions"]!["canDecideReopen"].Truthy(), "The higher committee is offered the decision");
        t.Check((await committee.Get($"/api/forms?cycleId={cycleId}&awaitingMe=true")).Items().Any(f => f["id"].Str() == form["id"].Str()),
            "The pending request is in the committee queue");
        (status, _) = await committee.Call("POST", formPath + "/reopen/reject", Scenario.O(("note", "")));
        t.Check(status == 400, "A rejection needs a reason");
        var rejected = await committee.Ok("POST", formPath + "/reopen/reject", Scenario.O(("note", "التصحيح غير مبرر في هذه الدورة")));
        t.Check(rejected["status"].Str() == "Approved" && rejected["reopen"]!["status"].Str() == "Rejected", "A rejected request leaves the form approved");
        await planner.Ok("POST", formPath + "/reopen", await planner.Versioned(formPath, Scenario.O(("note", "تصحيح مؤشر بعد مراجعة اللجنة العليا"))));
        (status, err) = await committee.Call("POST", formPath + "/reopen/approve", Scenario.O(("decisionRef", "")));
        t.Check(status == 400 && err.Code() == "decision_ref_required", "Approval needs the committee decision reference");
        var reopened = await committee.Ok("POST", formPath + "/reopen/approve", Scenario.O(("decisionRef", "محضر 7")));
        t.Check(reopened["status"].Str() == "InProgress" && reopened["step"].Str() == "Entry" && reopened["reopen"]!["status"].Str() == "Approved",
            "The committee approved with a decision reference and the form reopened");
        t.Check(reopened.ItemsOf().Where(i => i["value"] is not null).All(i => i["approvedBefore"] is not null), "Every value keeps its approved snapshot");
        var template = await entry.Download($"/api/forms/{form["id"].Str()}/template.xlsx");
        t.Check(template.Status == 200 && template.Body.AsSpan().StartsWith("PK"u8), "The form's Excel template downloaded");

        byte[] Edited(Action<IXLWorksheet, Dictionary<string, int>> change)
        {
            using var wb = new XLWorkbook(new MemoryStream(template.Body));
            var ws = wb.Worksheets.First();
            var columns = ws.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
            change(ws, columns);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
        int? targetRow = null; string? targetCode = null;
        using (var wb = new XLWorkbook(new MemoryStream(template.Body)))
        {
            var ws = wb.Worksheets.First();
            var col = ws.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
            for (var r = 2; r <= (ws.LastRowUsed()?.RowNumber() ?? 1); r++)
            {
                var code = ws.Cell(r, col["الرمز"]).GetString();
                var item = items.FirstOrDefault(i => i["code"].Str() == code);
                if (item is not null && !item["usesRatio"].Truthy() && item["unit"].Str()?.Trim() != "%" && item["aggregation"].Str() != "WeightedAverage") { targetRow = r; targetCode = code; break; }
            }
        }
        var good = Edited((ws, col) =>
        {
            if (targetRow is { } r) { ws.Cell(r, col["القيمة"]).Value = "١٣"; ws.Cell(r, col["التفسير"]).Value = "تحديث من الاستيراد"; } // Arabic-Indic digits
        });
        var bad = Edited((ws, col) => { ws.Cell(2, col["القيمة"]).Value = "abc"; ws.Cell(2, col["البسط"]).Value = "abc"; });
        var importPath = $"/api/forms/{form["id"].Str()}/import";
        (status, var pv) = await entry.Upload(importPath, "form.xlsx", bad);
        t.Check(status == 200 && pv!["errors"].Num() >= 1 && !pv["canApply"].Truthy(), "The preview shows the non-numeric cells");
        (status, err) = await entry.Upload(importPath + "?apply=true", "form.xlsx", bad);
        t.Check(status == 400 && err.Code() == "import_errors", "A file with errors applies nothing");
        if (targetRow is not null)
        {
            (status, pv) = await entry.Upload(importPath, "form.xlsx", good);
            t.Check(status == 200 && pv!["changes"].Num() == 1 && pv["canApply"].Truthy(), "The preview: one valid change");
            (status, _) = await entry.Upload(importPath + "?apply=true", "form.xlsx", good);
            t.Check(status == 200, "The import applied");
            var f2 = (await entry.Get($"/api/forms/{form["id"].Str()}"))!;
            var value = f2.ItemsOf().First(i => i["code"].Str() == targetCode)["value"]!["value"].Num();
            t.Check(value is { } v13 && Math.Abs((double)v13 - 13) < 0.001, "The imported value was saved (١٣ = 13)");
        }
        (status, _) = await entry.Upload(importPath, "x.xlsx", "not excel"u8.ToArray());
        t.Check(status == 400, "A file that is not Excel is refused");
        var during = (await planner.Get($"/api/results?cycleId={cycleId}&unitId={eng["id"].Str()}"))!;
        var officialDuring = EngActuals(during);
        t.Check(officialDuring.Count == officialBefore.Count && officialBefore.All(kv => officialDuring.GetValueOrDefault(kv.Key) == kv.Value),
            $"While under correction the results keep the approved values ({officialDuring.Count} indicators), not the edited ones");
        t.Check(during["formsUnderCorrection"].Num() >= 1, "The results show the form as under correction");

        form = await entry.Ok("POST", $"/api/forms/{form["id"].Str()}/submit", await entry.Versioned($"/api/forms/{form["id"].Str()}"));
        foreach (var who in new[] { college, dean, quality }) form = await who.Ok("POST", $"/api/forms/{form["id"].Str()}/approve", await who.Versioned($"/api/forms/{form["id"].Str()}", Scenario.O(("note", null))));
        t.Check(form["status"].Str() == "Approved", "The form was approved again after the correction");
        t.Check((await planner.Get($"/api/results?cycleId={cycleId}&unitId={eng["id"].Str()}"))!["formsUnderCorrection"].Num() == 0,
            "After re-approval the form is no longer under correction");

        t.Step("6. Results: approved values against the target, scope and export");
        var res = (await planner.Get($"/api/results?cycleId={cycleId}"))!;
        t.Check(res["unit"]!["kind"].Str() == "University", "The plan manager sees the university");
        t.Check(res["formsApproved"].Num() >= 1, $"Approved forms: {res["formsApproved"].Num()}/{res["formsTotal"].Num()}");
        t.Check(res["indicators"].Items().Any(r => r["actual"].Truthy() || r["actual"].Num() is not null), "Indicators have a value at university level");
        t.Check(res["heatUnits"].Items().Any(h => h["id"].Str() == eng["id"].Str()), "The college heat map includes Engineering");
        var resEng = (await planner.Get($"/api/results?cycleId={cycleId}&unitId={eng["id"].Str()}"))!;
        var engVals = resEng["indicators"].Items().Where(r => r["actual"].Num() is not null).ToList();
        t.Check(engVals.Count > 0, "Engineering results come from its approved form");
        t.Check(resEng["indicators"].Items().All(r => r["summary"].Truthy()), "Every indicator has a readable summary sentence");
        var dres = (await dean.Get($"/api/results?cycleId={cycleId}"))!;
        t.Check(dres["unit"]!["id"].Str() == eng["id"].Str()
            && dres["units"].Items().All(u => u["id"].Str() == eng["id"].Str()), "The Engineering dean sees only Engineering results");
        t.Check(!dres["canExport"].Truthy(), "The dean cannot export");
        var detail = (await planner.Get($"/api/results/{cycleId}/indicators/{engVals[0]["indicatorId"].Str()}?unitId={eng["id"].Str()}"))!;
        t.Check(detail["trend"].Items().Any(), "The indicator trend across cycles");
        var export = await planner.Download($"/api/results/{cycleId}/export.xlsx");
        t.Check(export.Status == 200 && export.Body.AsSpan().StartsWith("PK"u8), "The results sheet exported to Excel");
        t.Check((await dean.Download($"/api/results/{cycleId}/export.xlsx")).Status == 403, "Export is a special permission only");

        t.Step("7. Add a new indicator to the dictionary with an automatic code");
        var name = $"مؤشر تجريبي {Calls.Tag(5)}";
        (status, _) = await viewer.Call("POST", "/api/kpi/indicators", Scenario.O(("standardCode", "10"), ("nameAr", name), ("unit", "%"), ("level", "College"), ("reason", null)));
        t.Check(status == 403, "The viewer cannot add an indicator");
        var created = await planner.Ok("POST", "/api/kpi/indicators", Scenario.O(("standardCode", "10"), ("nameAr", name), ("unit", "%"), ("level", "College"), ("reason", "اختبار")));
        t.Check(created["code"].Str()!.StartsWith("10.", StringComparison.Ordinal) && created["current"]!["status"].Str() == "Draft", $"{created["code"].Str()} created as a draft");
        (status, err) = await planner.Call("POST", "/api/kpi/indicators", Scenario.O(("standardCode", "10"), ("nameAr", name), ("unit", "%"), ("level", "College"), ("reason", null)));
        t.Check(status == 422 && err.Code() == "duplicate_name", "A name is not repeated in the same standard");

        t.Step("8. Closing and mail");
        (status, _) = await planner.Call("POST", $"/api/cycles/{cycleId}/close", Scenario.O(("note", "")));
        t.Check(status == 400, "Closing asks for a reason");
        var closed = await planner.Ok("POST", $"/api/cycles/{cycleId}/close", Scenario.O(("note", "انتهت فترة التجربة")));
        t.Check(closed["status"].Str() == "Closed", "The cycle was closed");
        var others = (await entry.Get($"/api/forms?cycleId={cycleId}")).Items().Where(f => f["status"].Str() != "Approved").ToList();
        if (others.Count > 0)
        {
            (status, _) = await entry.Call("PUT", $"/api/forms/{others[0]["id"].Str()}/values", Scenario.O(("indicatorId", items[0]["indicatorId"]), ("kind", "Number"), ("value", 1)));
            t.Check(status is 400 or 409 or 422, "No entry after the cycle closes");
        }
        for (var i = 0; i < 20 && MailCount() <= mailBefore; i++) await Task.Delay(500);
        t.Check(MailCount() > mailBefore, "Mail notifications were sent to whoever has an action");
        t.Finish();
    }
}
