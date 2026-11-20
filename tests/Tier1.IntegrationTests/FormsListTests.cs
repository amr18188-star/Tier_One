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

    [Fact]
    public async Task Scope_search_and_status_filtered_in_sql_match_the_whole_list()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var planner = await app.As("planner");
        var all = (await planner.Get($"/api/forms?cycleId={cycle.Id}")).Items().ToList();
        Assert.NotEmpty(all);
        string Ids(IEnumerable<JsonNode> rows) => string.Join(",", rows.Select(r => r["id"].Str()).Order(StringComparer.Ordinal));

        // A college user sees part of the university's forms, and nothing outside it.
        var college = (await (await app.As("entry")).Get($"/api/forms?cycleId={cycle.Id}")).Items().ToList();
        Assert.NotEmpty(college);
        Assert.True(college.Count < all.Count);
        Assert.Subset(all.Select(r => r["id"].Str()).ToHashSet(), college.Select(r => r["id"].Str()).ToHashSet());

        // The search matches the unit or its parent, as the in-memory filter did.
        var term = college[0]["parentName"].Str() ?? college[0]["unitName"].Str()!;
        var searched = (await planner.Get($"/api/forms?cycleId={cycle.Id}&q={Uri.EscapeDataString(term)}")).Items().ToList();
        Assert.NotEmpty(searched);
        Assert.Equal(Ids(all.Where(r => r["unitName"].Str()!.Contains(term, StringComparison.Ordinal) || (r["parentName"].Str() ?? "").Contains(term, StringComparison.Ordinal))), Ids(searched));

        var notStarted = (await planner.Get($"/api/forms?cycleId={cycle.Id}&status=NotStarted")).Items().ToList();
        Assert.Equal(Ids(all.Where(r => r["status"].Str() == "NotStarted")), Ids(notStarted));
        Assert.Empty((await planner.Get($"/api/forms?cycleId={cycle.Id}&status=Approved")).Items());

        // Counts are computed for the rows returned: each form's required count is unchanged by the filters.
        var required = all.ToDictionary(r => r["id"].Str()!, r => r["required"]!.GetValue<int>());
        Assert.All(searched, r => Assert.Equal(required[r["id"].Str()!], r["required"]!.GetValue<int>()));
    }
}
