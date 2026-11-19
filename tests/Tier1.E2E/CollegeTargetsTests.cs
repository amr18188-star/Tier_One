using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// One set of university targets; each college sees its own targets beside them, never another college's, and in count
/// indicators its share of the university target. It removes what it creates, so it repeats on one database.
/// </summary>
public sealed class CollegeTargetsTests(ITestOutputHelper output)
{
    [E2EFact]
    public async Task A_college_sees_its_own_targets_beside_the_university_and_never_another_college()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var dean = await Env.As("dean");
        var units = await Env.UnitsAsync(planner);
        var eng = units["ENG"];
        var cs = units["CS"];
        const string code = "1.06";
        const int year = 2027;

        t.Step("1. The distribution: the plan manager sees every college, the Engineering dean only Engineering");
        var existing = (await planner.Get($"/api/kpi/indicators/{code}/targets"))!["colleges"].Items()
            .FirstOrDefault(r => r["unitId"].Str() == cs["id"].Str() && r["year"].Num() == year);
        var created = existing is null
            ? await planner.Ok("PUT", $"/api/kpi/indicators/{code}/targets", Scenario.O(("unitId", cs["id"]), ("year", year), ("kind", "Target"), ("value", 7), ("source", "اختبار رؤية الكليات")))
            : null;
        try
        {
            var all = (await planner.Get($"/api/kpi/indicators/{code}/distribution?year={year}"))!;
            t.Check(!all["ownUnitsOnly"].Truthy() && all["shares"].Items().Any(s => s["unitId"].Str() == cs["id"].Str()),
                "The plan manager sees the Computers college target in the distribution");
            var mine = (await dean.Get($"/api/kpi/indicators/{code}/distribution?year={year}"))!;
            var seen = mine["shares"].Items().Select(s => s["unitId"].Str()).ToList();
            t.Check(mine["ownUnitsOnly"].Truthy() && seen.All(id => id == eng["id"].Str()) && !seen.Contains(cs["id"].Str()),
                $"The Engineering dean sees only Engineering ({seen.Count} row(s)), not the Computers college");
            t.Check(mine["result"]!["collegeCount"].Num() == all["result"]!["collegeCount"].Num() && mine["result"]!["universityValue"].Num() == all["result"]!["universityValue"].Num(),
                "But the overall picture (university target, colleges counted) is the same for both");
            var targets = (await dean.Get($"/api/kpi/indicators/{code}/targets"))!;
            t.Check(targets["colleges"].Items().All(r => r["unitId"].Str() == eng["id"].Str()) && targets["university"].Items().Any(),
                "The targets page shows the dean the university targets and Engineering targets only");
        }
        finally
        {
            if (created is not null)
                await planner.Ok("POST", $"/api/kpi/targets/{created["id"].Str()}/delete", Scenario.O(("reason", "تنظيف اختبار رؤية الكليات")), expect: 204);
        }

        t.Step("2. Results: next to the university target for ratios, and as a share of it for counts");
        var cycle = (await planner.Get("/api/cycles")).Items()
            .Where(c => !c["trial"].Truthy() && c["status"].Str() is "Open" or "Closed" && c["targetYear"].Num() == year).First();
        var results = (await dean.Get($"/api/results?cycleId={cycle["id"].Str()}"))!;
        t.Check(results["units"].Items().All(u => u["id"].Str() == eng["id"].Str()), "The dean results are Engineering only");
        var rows = results["indicators"].Items().ToList();
        var shares = rows.Where(r => r["universityTotalTarget"].Num() is not null).ToList();
        t.Check(shares.Count > 0, $"Count indicators carry the university total target ({shares.Count})");
        t.Check(shares.All(r => r["universityTarget"].Num() is null), "Shares are for counts only; ratios compare with the university target itself");
        t.Check(shares.Where(r => r["target"].Num() is not null && r["targetShare"].Num() is not null)
                .All(r => Math.Abs((double)(r["targetShare"].Num()! - r["target"].Num()! / r["universityTotalTarget"].Num()!)) < 0.0001),
            "The share is the college target over the university target");
        var withShare = shares.FirstOrDefault(r => r["code"].Str() == code);
        if (withShare is not null)
            output.WriteLine($"   ({code}: Engineering target {withShare["target"].Num()} of university {withShare["universityTotalTarget"].Num()} = {withShare["targetShare"].Num()})");
        t.Finish();
    }
}
