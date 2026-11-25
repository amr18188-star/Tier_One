using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>
/// System integration review 2026-11-24, waves 2 and 3: the exceptions a user meets in common work (G-06, G-08, G-09, G-10) and
/// the latent state checks (G-13, G-14, G-16).
/// </summary>
[Collection("app")]
public sealed class IntegrationReviewWaveTwoTests(Tier1App app, ITestOutputHelper output)
{
    private static readonly string[] DefinitionFields =
        ["nameAr", "nameEn", "definition", "purpose", "exclusions", "numerator", "denominator", "unit", "direction", "frequency", "level", "aggregation",
         "aggregationNote", "dataSource", "ownerUnitId", "evidenceRequired", "formula", "accumulation"];

    /// <summary>G-06: a department representative opened results and progress on an empty list (their scope holds only departments).</summary>
    [Fact]
    public async Task G06_a_department_representative_sees_the_results_and_progress_of_their_department()
    {
        var t = new Scenario(output);
        var cycle = await Flow.OpenCycleAsync(app);
        var rep = await app.As("deptrep");
        var (results, body) = await rep.Call("GET", $"/api/results?cycleId={cycle.Id}");
        t.Check(results == 200, $"Results open for the representative ({results})");
        t.Check(body?["units"].Items().Any() == true || body?["unit"] is not null, "Results name the representative's department");
        var (progress, _) = await rep.Call("GET", "/api/progress");
        t.Check(progress == 200, $"Progress opens for the representative ({progress})");
        t.Finish();
    }

    /// <summary>G-08: the draft of a new version of an approved definition can be dropped; the approved version stays in force.</summary>
    [Fact]
    public async Task G08_a_new_version_draft_is_discarded_and_the_approved_version_stays()
    {
        var t = new Scenario(output);
        var planner = await app.As("planner");
        var code = (await planner.Get("/api/kpi/indicators")).Items().First(i => i["status"].Str() == "Approved" && !i["suspended"].Truthy())["code"].Str()!;
        var before = (await planner.Get($"/api/kpi/indicators/{code}"))!;
        var number = before["current"]!["number"].Num();

        var (none, noneError) = await planner.Call("POST", $"/api/kpi/indicators/{code}/discard-draft", Scenario.O(("reason", "لا مسودة لتجاهلها")));
        t.Check((none, noneError.Code()) == (422, "no_draft"), $"No draft, nothing to discard ({none} {noneError.Code()})");

        var body = new JsonObject();
        foreach (var f in DefinitionFields) body[f] = before["current"]![f].Copy();
        body["changeReason"] = "اختبار تجاهل مسودة إصدار جديد";
        body["version"] = null;
        var draft = await planner.Ok("PUT", $"/api/kpi/indicators/{code}/definition", body);
        t.Check(draft["current"]!["status"].Str() == "Draft" && draft["actions"]!["canDiscard"].Truthy(), "Editing the approved definition makes a draft that can be discarded");

        var after = await planner.Ok("POST", $"/api/kpi/indicators/{code}/discard-draft", Scenario.O(("reason", "التعديل لم يعد مطلوبًا")));
        t.Check(after["current"]!["status"].Str() == "Approved" && after["current"]!["number"].Num() == number, "The approved version is in force again");
        t.Check(!after["actions"]!["canDiscard"].Truthy(), "Nothing left to discard");
        t.Finish();
    }

    /// <summary>G-08: a cycle never opened can be deleted, which frees its quarter; an open one cannot.</summary>
    [Fact]
    public async Task G08_a_draft_cycle_is_deleted_and_an_open_one_is_not()
    {
        var t = new Scenario(output);
        var planner = await app.As("planner");
        var label = $"del-{Calls.Tag()}";
        var draft = await planner.Ok("POST", "/api/cycles", Scenario.O(("name", $"دورة للحذف {label}"), ("periodLabel", label), ("targetYear", 2027),
            ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", true), ("version", null)));
        var (deleted, _) = await planner.Call("POST", $"/api/cycles/{draft["id"].Str()}/delete", Scenario.O(("note", "أنشئت بالخطأ للاختبار")));
        t.Check(deleted == 204, $"The draft cycle is deleted ({deleted})");
        t.Check((await planner.Get("/api/cycles")).Items().All(c => c["id"].Str() != draft["id"].Str()), "It is gone from the list");

        var open = await Flow.OpenCycleAsync(app);
        var (refused, error) = await planner.Call("POST", $"/api/cycles/{open.Id}/delete", Scenario.O(("note", "محاولة حذف دورة مفتوحة")));
        t.Check(refused >= 400 && error.Code() == "cycle_not_draft", $"An open cycle is not deleted ({refused} {error.Code()})");
        t.Finish();
    }

    /// <summary>G-09 and G-16: suspending is offered on the page, and suspending twice is refused instead of writing a second entry.</summary>
    [Fact]
    public async Task G09_an_indicator_is_suspended_once_and_reactivated()
    {
        var t = new Scenario(output);
        var planner = await app.As("planner");
        var code = (await planner.Get("/api/kpi/indicators")).Items().Last(i => i["status"].Str() == "Approved" && !i["suspended"].Truthy())["code"].Str()!;
        var detail = (await planner.Get($"/api/kpi/indicators/{code}"))!;
        var suspender = detail["actions"]!["canSuspend"].Truthy() ? planner : await app.As("sa1");
        var off = await suspender.Ok("POST", $"/api/kpi/indicators/{code}/suspend", Scenario.O(("suspended", true), ("reason", "اختبار الإيقاف")));
        t.Check(off["suspended"].Truthy(), "Suspended");
        var (again, error) = await suspender.Call("POST", $"/api/kpi/indicators/{code}/suspend", Scenario.O(("suspended", true), ("reason", "اختبار الإيقاف مرة ثانية")));
        t.Check((again, error.Code()) == (400, "no_change"), $"Suspending again is refused ({again} {error.Code()})");
        var on = await suspender.Ok("POST", $"/api/kpi/indicators/{code}/suspend", Scenario.O(("suspended", false), ("reason", "انتهى الاختبار")));
        t.Check(!on["suspended"].Truthy(), "Reactivated");
        t.Finish();
    }

    /// <summary>G-09: the target page says who may delete a target.</summary>
    [Fact]
    public async Task G09_target_actions_say_who_may_delete()
    {
        var planner = await app.As("planner");
        var code = (await planner.Get("/api/kpi/indicators")).Items().First(i => i["status"].Str() == "Approved")["code"].Str()!;
        var actions = (await planner.Get($"/api/kpi/indicators/{code}/targets"))!["actions"]!;
        Assert.NotNull(actions["canDeleteUniversity"]);
        Assert.NotNull(actions["canDeleteColleges"]);
        var viewer = await app.As("president");
        var seen = (await viewer.Get($"/api/kpi/indicators/{code}/targets"))!["actions"]!;
        Assert.False(seen["canDeleteColleges"].Truthy());
    }

    /// <summary>G-10: the scorecard names the indicators the council's workbook has no row for, for whoever may export it.</summary>
    [Fact]
    public async Task G10_the_scorecard_names_the_indicators_left_out_of_the_council_workbook()
    {
        foreach (var account in new[] { "tier1", "planner", "sa1" })
        {
            var card = (await (await app.As(account)).Get("/api/scorecard"))!;
            if (!card["canExport"].Truthy()) continue;
            var omitted = card["exportOmitted"].Items().Select(n => n.Str()).ToList();
            Assert.DoesNotContain("1.01", omitted);
            return;
        }
        Assert.Fail("No demo account may export the council workbook");
    }

    /// <summary>G-13: the evidence of a closed cycle stays as it was, like its values.</summary>
    [Fact]
    public async Task G13_evidence_is_not_voided_after_the_cycle_closes()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var item = form.ItemsOf().First();
        var entry = await app.As("entry");
        await entry.Ok("PUT", $"/api/forms/{form["id"].Str()}/values", Calls.InputFor(item));
        var (uploaded, doc) = await entry.Upload($"/api/forms/{form["id"].Str()}/values/{item["indicatorId"].Str()}/evidence", "evidence.pdf", Env.UniquePdf());
        Assert.Equal(200, uploaded);

        var planner = await app.As("planner");
        var (closed, closeError) = await planner.Call("POST", $"/api/cycles/{cycle.Id}/close", Scenario.O(("note", "إغلاق للاختبار")));
        Assert.True(closed == 200, $"close: {closed} {closeError}");
        var (voided, error) = await entry.Call("POST", $"/api/documents/{doc!["id"].Str()}/void", Scenario.O(("note", "إبطال بعد الإغلاق")));
        Assert.Equal((422, "cycle_not_open"), (voided, error.Code()));
    }

    /// <summary>G-14: applying a file previewed before a value changed asks for a new preview instead of writing over the change.</summary>
    [Fact]
    public async Task G14_an_import_previewed_before_a_change_is_not_applied_over_it()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var id = form["id"].Str();
        var items = form.ItemsOf().ToList();
        var entry = await app.As("entry");
        await entry.Ok("PUT", $"/api/forms/{id}/values", Calls.InputFor(items[0]));

        var (got, file, _, _) = await entry.Download($"/api/forms/{id}/template.xlsx");
        Assert.Equal(200, got);
        var (previewed, preview) = await entry.Upload($"/api/forms/{id}/import", "form.xlsx", file);
        Assert.Equal(200, previewed);
        var stamp = preview!["stamp"].Str();
        Assert.False(string.IsNullOrEmpty(stamp));

        await entry.Ok("PUT", $"/api/forms/{id}/values", Calls.InputFor(items[1]));
        var (applied, error) = await entry.Upload($"/api/forms/{id}/import?apply=true&stamp={Uri.EscapeDataString(stamp!)}", "form.xlsx", file);
        Assert.Equal((409, "import_stale"), (applied, error.Code()));
    }
}
