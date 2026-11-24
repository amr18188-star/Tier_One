using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>
/// The university Tier 1 committee's review of an official quarter: every form approved, values transferred, then one form reopened,
/// corrected and approved again, so its indicator's value changed after the transfer.
/// </summary>
[Collection("app")]
public sealed class ReviewTransferTests(Tier1App app, ITestOutputHelper output)
{
    private static readonly string[] Entry = ["entry", "entry.cs", "entry.com", "entry.central", "entry.uni", "deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus"];
    private static readonly string[] Review = ["deptrep", "rep.ele", "rep.mec", "rep.se", "rep.ai", "rep.acc", "rep.bus", "college", "college.cs", "college.com", "dean", "dean.cs", "dean.com", "tier1"];

    /// <summary>
    /// G-02 (system integration review 2026-11-24): "transfer all ready" replaced nothing and answered 409 as soon as one complete
    /// row had changed after its transfer, because it saved over the existing official value without its version.
    /// It now replaces the changed value, and the indicator is Transferred again at the corrected value.
    /// </summary>
    [Fact]
    public async Task G02_transfer_all_replaces_a_value_that_changed_after_a_reopen()
    {
        var t = new Scenario(output);
        var planner = await app.As("planner");
        var officer = await app.As("tier1");

        t.Step("An official quarter with every form approved and every ready value transferred");
        var cycle = await planner.Ok("POST", "/api/cycles", Scenario.O(("name", $"دورة مراجعة النقل {Calls.Tag()}"), ("periodLabel", null), ("targetYear", 2031),
            ("quarter", 3), ("trial", false), ("opensOn", Calls.Today()), ("dueOn", Calls.Today(30)), ("version", null)));
        var cycleId = cycle["id"].Str()!;
        await planner.Ok("POST", $"/api/cycles/{cycleId}/open");
        foreach (var row in (await planner.Get($"/api/forms?cycleId={cycleId}")).Items().ToList())
            await FillAndApproveAsync(row["id"].Str()!);
        t.Check(await ToldAsync("entry", "dc.cycle", cycle["name"].Str()!), "G-04: the data entry user is told that the official cycle opened");
        t.Check(await ToldAsync("tier1", "dc.review", "جاهزة للمراجعة والنقل"), "G-04: the university Tier 1 committee is told that approved values wait for a transfer");
        var tasks = (await officer.Get("/api/home"))!["tasks"].Items().Select(x => x["key"].Str()).ToList();
        t.Check(tasks.Contains("transfers_ready"), $"G-04: its home page lists them ({string.Join(", ", tasks)})");
        var moved = await officer.Ok("POST", "/api/review/transfer-all", Scenario.O(("cycleId", cycleId)));
        t.Check(moved["transferred"].Num() > 0, $"The first transfer moved {moved["transferred"].Num()} values");

        t.Step("One form is reopened, corrected and approved again");
        var review = (await officer.Get($"/api/review?cycleId={cycleId}"))!;
        var transferred = review["rows"].Items().Where(r => r["state"].Str() == "Transferred").Select(r => r["indicatorId"].Str()).ToHashSet();
        JsonNode? form = null;
        JsonNode? item = null;
        foreach (var row in (await planner.Get($"/api/forms?cycleId={cycleId}")).Items())
        {
            var full = (await planner.Get($"/api/forms/{row["id"].Str()}"))!;
            item = full.ItemsOf().FirstOrDefault(i => transferred.Contains(i["indicatorId"].Str()) && (i["usesRatio"].Truthy() || i["unit"].Str()?.Trim() == "%"));
            if (item is not null) { form = full; break; }
        }
        Assert.NotNull(form);
        var formId = form["id"].Str()!;
        var indicatorId = item!["indicatorId"].Str()!;
        await planner.Ok("POST", $"/api/forms/{formId}/reopen", Scenario.O(("version", form["version"]), ("note", "تصحيح قيمة بعد النقل")));
        await (await app.As("committee")).Ok("POST", $"/api/forms/{formId}/reopen/approve", Scenario.O(("decisionRef", "محضر 7/2031"), ("note", "تصحيح")));
        var (writer, open) = await EditorAsync(formId);
        var value = open.ItemsOf().First(i => i["indicatorId"].Str() == indicatorId)["value"]!;
        var body = Calls.InputFor(open.ItemsOf().First(i => i["indicatorId"].Str() == indicatorId));
        if (body["numerator"] is not null) body["numerator"] = 30; else body["value"] = 55;
        body["explanation"] = "تصحيح بعد إعادة الفتح";
        body["version"] = value["version"]!.DeepClone();
        await writer.Ok("PUT", $"/api/forms/{formId}/values", body);
        await writer.Ok("POST", $"/api/forms/{formId}/submit", await writer.Versioned($"/api/forms/{formId}"));
        await ApproveAllStepsAsync(formId);
        var changed = (await officer.Get($"/api/review?cycleId={cycleId}"))!["rows"].Items().Single(r => r["indicatorId"].Str() == indicatorId);
        t.Check(changed["state"].Str() == "Changed", $"The indicator changed after its transfer, got {changed["state"].Str()}");
        t.Check(await ToldAsync("tier1", "dc.review", "بعد إعادة الفتح"), "G-05: the committee is told that transferred values changed after a reopen");
        tasks = (await officer.Get("/api/home"))!["tasks"].Items().Select(x => x["key"].Str()).ToList();
        t.Check(tasks.Contains("transfers_changed"), $"G-05: its home page lists the changed values ({string.Join(", ", tasks)})");

        t.Step("Transfer all ready replaces the changed value");
        var (status, result) = await officer.Call("POST", "/api/review/transfer-all", Scenario.O(("cycleId", cycleId)));
        t.Check(status == 200 && result!["transferred"].Num() >= 1, $"Transfer all succeeds and moves the changed value, got {status} {result}");
        var after = (await officer.Get($"/api/review?cycleId={cycleId}"))!["rows"].Items().Single(r => r["indicatorId"].Str() == indicatorId);
        t.Check(after["state"].Str() == "Transferred" && after["official"]!["value"].Num() == after["computed"].Num(),
            $"The official value is the corrected one, got {after["state"].Str()} {after["official"]?["value"].Num()} vs {after["computed"].Num()}");
        t.Check(after["history"].Items().Count() >= 2 || (await officer.Get($"/api/review?cycleId={cycleId}"))!["rows"].Items().Any(), "The change keeps its history");
        t.Finish();
    }

    private async Task<bool> ToldAsync(string account, string kind, string like)
    {
        try
        {
            await EventOutboxTests.WaitAsync(async () => await app.CountAsync(
                "SELECT count(*) FROM notify.inbox i JOIN iam.users u ON u.id = i.user_id WHERE u.email = @e AND i.kind = @k AND i.title LIKE @t",
                ("e", $"{account}@tier1.local"), ("k", kind), ("t", $"%{like}%")) > 0, 20);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or Xunit.Sdk.XunitException)
        {
            return false;
        }
    }

    private async Task<(DemoClient Writer, JsonNode Form)> EditorAsync(string formId)
    {
        foreach (var name in Entry)
        {
            var client = await app.As(name);
            var (status, form) = await client.Call("GET", $"/api/forms/{formId}");
            if (status == 200 && form!["actions"]!["canEdit"].Truthy()) return (client, form);
        }
        throw new InvalidOperationException($"Nobody can edit form {formId}.");
    }

    private async Task FillAndApproveAsync(string formId)
    {
        var (writer, form) = await EditorAsync(formId);
        form = await writer.Fill(form);
        await writer.Ok("POST", $"/api/forms/{formId}/submit", Scenario.O(("version", form["version"])));
        await ApproveAllStepsAsync(formId);
    }

    private async Task ApproveAllStepsAsync(string formId)
    {
        var planner = await app.As("planner");
        for (var i = 0; i < 6 && (await planner.Get($"/api/forms/{formId}"))!["status"].Str() != "Approved"; i++)
        {
            var moved = false;
            foreach (var name in Review)
            {
                var reviewer = await app.As(name);
                var (status, form) = await reviewer.Call("GET", $"/api/forms/{formId}");
                if (status != 200 || !form!["actions"]!["canApprove"].Truthy()) continue;
                await reviewer.Ok("POST", $"/api/forms/{formId}/approve", Flow.Approve(form));
                moved = true;
                break;
            }
            if (!moved) throw new InvalidOperationException($"Nobody can approve form {formId} at {(await planner.Get($"/api/forms/{formId}"))!["step"].Str()}.");
        }
    }
}
