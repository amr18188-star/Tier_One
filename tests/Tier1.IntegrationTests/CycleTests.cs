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

    [Fact]
    public async Task Two_official_cycles_for_one_quarter_at_once_leave_one()
    {
        // DB-3: the service checks first, and the database index catches the two that pass the check together.
        var planner = await app.As("planner");
        var year = 2031;
        JsonObject Body(string tag) => Scenario.O(("name", $"دورة رسمية {tag}"), ("periodLabel", tag), ("targetYear", year), ("opensOn", Calls.Today()),
            ("dueOn", Calls.Today(14)), ("trial", false), ("quarter", 4), ("version", null));
        var both = await Task.WhenAll(planner.Call("POST", "/api/cycles", Body("a-" + Calls.Tag())), planner.Call("POST", "/api/cycles", Body("b-" + Calls.Tag())));
        Assert.Equal([200, 409], both.Select(r => r.Status).Order());
        Assert.Equal("quarter_taken", both.Single(r => r.Status == 409).Body.Code());
        var official = (await planner.Get("/api/cycles")).Items().Count(c => c["targetYear"].Num() == year && c["quarter"].Num() == 4 && !c["trial"].Truthy());
        Assert.Equal(1, official);
    }

    private async Task<string> DueDatesAsync(string cycleId)
    {
        var planner = await app.As("planner");
        return string.Join(',', (await planner.Get($"/api/forms?cycleId={cycleId}")).Items().Select(f => f["dueOn"].Str()).Distinct().Order(StringComparer.Ordinal));
    }
}
