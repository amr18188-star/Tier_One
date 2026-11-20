using Microsoft.Extensions.DependencyInjection;
using Tier1.Application.Collection;

namespace Tier1.IntegrationTests;

/// <summary>PERF-B3: a cycle's results snapshot is reused across requests, and any change to the rows it reads rebuilds it.</summary>
[Collection("app")]
public sealed class ResultsCacheTests(Tier1App app)
{
    [Fact]
    public async Task A_closed_cycle_is_built_once_and_rebuilt_when_a_unit_is_renamed()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var planner = await app.As("planner");
        await planner.Ok("POST", $"/api/cycles/{cycle.Id}/close", Scenario.O(("note", "اختبار ذاكرة النتائج")));
        var cache = app.Services.GetRequiredService<CycleSnapshotCache>();
        var engineering = await Flow.UnitIdAsync(app, "ENG");
        string NameOf(JsonNode? results) => results!["units"]!.AsArray().Single(u => u!["id"].Str() == engineering)!["name"].Str()!;

        var first = await planner.Get($"/api/results?cycleId={cycle.Id}");
        var built = cache.Builds;
        var second = await planner.Get($"/api/results?cycleId={cycle.Id}");
        Assert.Equal(built, cache.Builds);
        Assert.Equal(first!.ToJsonString(), second!.ToJsonString());

        var name = NameOf(first);
        try
        {
            await app.ScalarAsync("UPDATE org.units SET name_ar = name_ar || ' (مؤقت)' WHERE id = @id", ("id", Guid.Parse(engineering)));
            var renamed = await planner.Get($"/api/results?cycleId={cycle.Id}");
            Assert.True(cache.Builds > built, "A renamed unit changes the fingerprint, so the snapshot is rebuilt");
            Assert.Equal(name + " (مؤقت)", NameOf(renamed));
        }
        finally
        {
            await app.ScalarAsync("UPDATE org.units SET name_ar = @name WHERE id = @id", ("name", name), ("id", Guid.Parse(engineering)));
        }
    }

    [Fact]
    public async Task The_fingerprint_changes_with_each_table_a_snapshot_reads()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1.Application.Abstractions.ICollectionStore>();
        var id = Guid.Parse(cycle.Id);
        async Task<string> Print() => (await CycleSnapshotCache.FingerprintsAsync(db, [id], default))[id];

        var before = await Print();
        Assert.Equal(before, await Print());
        await app.ScalarAsync("UPDATE kpi.standards SET name_ar = name_ar WHERE id = (SELECT id FROM kpi.standards ORDER BY id LIMIT 1)");
        var afterStandard = await Print();
        Assert.NotEqual(before, afterStandard);
        await app.ScalarAsync("UPDATE dc.cycles SET name = name WHERE id = @id", ("id", id));
        Assert.NotEqual(afterStandard, await Print());
    }
}
