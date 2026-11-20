namespace Tier1.IntegrationTests;

/// <summary>PERF-B2/BE-17: without a cycle the forms list holds the open cycles only; a closed cycle's forms are listed when it is asked for.</summary>
[Collection("app")]
public sealed class FormsListTests(Tier1App app)
{
    [Fact]
    public async Task A_closed_cycle_leaves_the_default_list_and_stays_reachable_by_id()
    {
        var open = await Flow.OpenCycleAsync(app);
        var closed = await Flow.OpenCycleAsync(app);
        var planner = await app.As("planner");
        await planner.Ok("POST", $"/api/cycles/{closed.Id}/close", Scenario.O(("note", "اختبار قائمة النماذج")));

        var cyclesInDefault = (await planner.Get("/api/forms")).Items().Select(f => f["cycleId"].Str()).ToHashSet();
        Assert.Contains(open.Id, cyclesInDefault);
        Assert.DoesNotContain(closed.Id, cyclesInDefault);

        var closedForms = (await planner.Get($"/api/forms?cycleId={closed.Id}")).Items().ToList();
        Assert.NotEmpty(closedForms);
        Assert.All(closedForms, f => Assert.Equal(closed.Id, f["cycleId"].Str()));
        Assert.All(closedForms, f => Assert.False(f["awaitingMe"].Truthy(), "A closed cycle's form awaits no one"));
    }
}
