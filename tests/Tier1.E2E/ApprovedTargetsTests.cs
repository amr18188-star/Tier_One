using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// D3 (ADR-0003): performance is measured against approved targets only. A dean's proposed college target does not change the
/// college's results until the higher committee approves it; the results show it as pending. It removes what it creates, so it repeats on one database.
/// </summary>
public sealed class ApprovedTargetsTests(ITestOutputHelper output)
{
    [E2EFact]
    public async Task A_proposed_target_does_not_change_results_until_approved()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var dean = await Env.As("dean");
        var units = await Env.UnitsAsync(planner);
        var eng = units["ENG"];

        t.Step("1. A cycle and an indicator where engineering is measured against an approved target it did not set itself");
        var cycle = (await planner.Get("/api/cycles")).Items()
            .Where(c => !c["trial"].Truthy() && c["status"].Str() is "Open" or "Closed")
            .OrderByDescending(c => c["targetYear"].Num()).First();
        var year = (int)cycle["targetYear"].Num()!;
        var results = (await planner.Get($"/api/results?cycleId={cycle["id"].Str()}&unitId={eng["id"].Str()}"))!;
        JsonNode? chosen = null;
        JsonNode? proposed = null;
        decimal value = 0;
        foreach (var i in results["indicators"].Items().Where(i => i["target"].Num() is not null && i["targetSource"].Str() is "EachCollege" or "University"))
        {
            var rows = (await planner.Get($"/api/kpi/indicators/{i["code"].Str()}/targets"))!["colleges"].Items();
            if (rows.Any(r => r["unitId"].Str() == eng["id"].Str() && r["year"].Num() == year)) continue;
            var current = i["target"].Num()!.Value;
            value = Math.Max(1, Math.Round(current / 2, 0));
            if (value == current) value = current - 1;
            var (status, body) = await dean.Call("PUT", $"/api/kpi/indicators/{i["code"].Str()}/targets",
                Scenario.O(("unitId", eng["id"]), ("year", year), ("kind", "Target"), ("value", value), ("source", "مقترح العميد")));
            if (status != 200) continue;
            (chosen, proposed) = (i, body);
            break;
        }
        Assert.True(chosen is not null, "No engineering indicator measured against a common target without its own; reset the data.");
        output.WriteLine($"  (indicator {chosen!["code"].Str()}, cycle {cycle["name"].Str()}, approved target {chosen["target"].Num()}, proposed {value})");
        try
        {
            t.Check(proposed!["status"].Str() == "Proposed", "The dean's college target is saved as proposed");

            t.Step("2. The results keep the approved target and show the proposal as pending");
            var after = (await planner.Get($"/api/results?cycleId={cycle["id"].Str()}&unitId={eng["id"].Str()}"))!["indicators"].Items()
                .First(i => i["indicatorId"].Str() == chosen["indicatorId"].Str());
            t.Check(after["target"].Num() == chosen["target"].Num() && after["targetSource"].Str() == chosen["targetSource"].Str(),
                $"The college is still measured against the approved target ({after["target"].Num()}, {after["targetSource"].Str()})");
            t.Check(after["performance"].Str() == chosen["performance"].Str() && after["achievement"].Num() == chosen["achievement"].Num(),
                "Its performance and achievement do not change");
            t.Check(after["pendingTarget"].Num() == value && after["pendingTargetYear"].Num() == year, "The proposal is shown as pending approval");
        }
        finally
        {
            await planner.Ok("POST", $"/api/kpi/targets/{proposed!["id"].Str()}/delete", Scenario.O(("reason", "تنظيف اختبار المستهدفات المعتمدة")), expect: 204);
        }
        t.Finish();
    }
}
