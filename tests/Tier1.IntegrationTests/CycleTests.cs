namespace Tier1.IntegrationTests;

/// <summary>BE-4: a cycle edit refused for a stale version leaves every form's deadline where it was.</summary>
[Collection("app")]
public sealed class CycleTests(Tier1App app)
{
    [Fact]
    public async Task A_refused_deadline_change_moves_no_form()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var planner = await app.As("planner");
        var current = (await planner.Get("/api/cycles")).Items().Single(c => c["id"].Str() == cycle.Id);
        var body = Scenario.O(("name", current["name"]), ("periodLabel", current["periodLabel"]), ("targetYear", current["targetYear"]),
            ("opensOn", current["opensOn"]), ("dueOn", Calls.Today(30)), ("trial", current["trial"]), ("quarter", current["quarter"]));

        var before = await DueDatesAsync(cycle.Id);
        var (status, error) = await planner.Call("PUT", $"/api/cycles/{cycle.Id}", Scenario.With(body, ("version", current["version"].Num() - 1)));
        Assert.Equal((409, "conflict"), (status, error.Code()));
        Assert.Equal(before, await DueDatesAsync(cycle.Id));

        (status, error) = await planner.Call("PUT", $"/api/cycles/{cycle.Id}", Scenario.With(body, ("version", null)));
        Assert.Equal((428, "version_required"), (status, error.Code()));
        Assert.Equal(before, await DueDatesAsync(cycle.Id));

        await planner.Ok("PUT", $"/api/cycles/{cycle.Id}", Scenario.With(body, ("version", current["version"])));
        Assert.All((await DueDatesAsync(cycle.Id)).Split(','), d => Assert.Equal(Calls.Today(30), d));
    }

    private async Task<string> DueDatesAsync(string cycleId)
    {
        var planner = await app.As("planner");
        return string.Join(',', (await planner.Get($"/api/forms?cycleId={cycleId}")).Items().Select(f => f["dueOn"].Str()).Distinct().Order(StringComparer.Ordinal));
    }
}
