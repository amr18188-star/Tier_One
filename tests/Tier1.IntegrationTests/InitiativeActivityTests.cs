namespace Tier1.IntegrationTests;

/// <summary>ADR-0017: each initiative has its own activities, managed with the initiatives' permissions and audited.</summary>
[Collection("app")]
public sealed class InitiativeActivityTests(Tier1App app)
{
    private static JsonObject Activity(string title, string status = "Planned", int progress = 0, string? start = "2026-09-01", string? end = "2026-12-31", uint? version = null) =>
        Scenario.O(("titleAr", title), ("description", null), ("ownerUnitId", null), ("startDate", start), ("endDate", end),
            ("status", status), ("progress", progress), ("sortOrder", null), ("version", version is { } v ? (long)v : null));

    private async Task<(DemoClient Planner, string InitiativeId)> NewInitiativeAsync()
    {
        var planner = await app.As("planner");
        var tree = (await planner.Get("/api/strategy/tree"))!;
        var standard = tree["pillars"].Items().SelectMany(p => p["standards"].Items()).First();
        var initiative = await planner.Ok("POST", "/api/strategy/initiatives", Scenario.O(("standardId", standard["id"]), ("nameAr", $"مبادرة اختبار {Calls.Tag()}"),
            ("description", null), ("expectedOutcome", null), ("ownerUnitId", null), ("startYear", 2027), ("endYear", 2028), ("status", "Planned"),
            ("indicatorCodes", new JsonArray()), ("version", null)));
        return (planner, initiative["id"].Str()!);
    }

    /// <summary>AC-A1, AC-A2: activities belong to their initiative, in order; completing one sets its progress to 100.</summary>
    [Fact]
    public async Task An_initiative_has_its_own_activities_in_order()
    {
        var (planner, initiative) = await NewInitiativeAsync();
        var (_, other) = await NewInitiativeAsync();
        var first = await planner.Ok("POST", $"/api/strategy/initiatives/{initiative}/activities", Activity("ورشة تعريفية بالمبادرة"));
        await planner.Ok("POST", $"/api/strategy/initiatives/{initiative}/activities", Activity("إعداد دليل التنفيذ", "InProgress", 40));
        await planner.Ok("POST", $"/api/strategy/initiatives/{other}/activities", Activity("نشاط مبادرة أخرى"));

        var list = (await planner.Get($"/api/strategy/initiatives/{initiative}/activities")).Items().ToList();
        Assert.Equal(["ورشة تعريفية بالمبادرة", "إعداد دليل التنفيذ"], list.Select(a => a["titleAr"].Str()));

        var done = await planner.Ok("PUT", $"/api/strategy/activities/{first["id"].Str()}", Activity("ورشة تعريفية بالمبادرة", "Completed", 30, version: (uint)first["version"].Num()!));
        Assert.Equal(("Completed", 100m), (done["status"].Str(), done["progress"].Num()));

        // The initiative shows how many activities it has and their average completion: (100 + 40) / 2.
        var node = (await planner.Get("/api/strategy/tree"))!["pillars"].Items().SelectMany(p => p["standards"].Items())
            .SelectMany(s => s["initiatives"].Items()).Single(i => i["id"].Str() == initiative);
        Assert.Equal((2m, 70m), (node["activityCount"].Num(), node["activityProgress"].Num()));
    }

    /// <summary>AC-A3: invalid input names its field; nothing is saved.</summary>
    [Fact]
    public async Task Invalid_activities_are_refused_with_the_field_named()
    {
        var (planner, initiative) = await NewInitiativeAsync();
        var path = $"/api/strategy/initiatives/{initiative}/activities";
        foreach (var (body, field) in new[]
                 {
                     (Activity("  "), "titleAr"),
                     (Activity("نشاط", end: "2026-08-01"), "endDate"),
                     (Activity("نشاط", progress: 120), "progress"),
                 })
        {
            var (status, error) = await planner.Call("POST", path, body);
            Assert.Equal(400, status);
            Assert.Equal(field, error!["errors"]!.AsArray().First()!["field"].Str());
        }
        Assert.Empty((await planner.Get(path)).Items());
    }

    /// <summary>AC-A4: who may only view the plan cannot add; a deletion needs a reason; every change is audited; deleting the initiative removes its activities.</summary>
    [Fact]
    public async Task Activities_follow_the_initiatives_permissions_and_are_audited()
    {
        var (planner, initiative) = await NewInitiativeAsync();
        var viewer = await app.As("viewer");
        Assert.Equal(403, (await viewer.Call("POST", $"/api/strategy/initiatives/{initiative}/activities", Activity("نشاط"))).Status);

        var a = await planner.Ok("POST", $"/api/strategy/initiatives/{initiative}/activities", Activity("نشاط يحذف"));
        Assert.Equal(400, (await planner.Call("POST", $"/api/strategy/activities/{a["id"].Str()}/delete", Scenario.O(("reason", "")))).Status);
        Assert.Equal(204, (await planner.Call("POST", $"/api/strategy/activities/{a["id"].Str()}/delete", Scenario.O(("reason", "أدمج في نشاط آخر")))).Status);
        Assert.True(await app.CountAsync("SELECT count(*) FROM logs.outbox WHERE payload::text LIKE '%kpi.activity_created%' AND payload::text LIKE '%' || @id || '%'", ("id", a["id"].Str()!)) >= 1);
        Assert.True(await app.CountAsync("SELECT count(*) FROM logs.outbox WHERE payload::text LIKE '%kpi.activity_deleted%' AND payload::text LIKE '%' || @id || '%'", ("id", a["id"].Str()!)) >= 1);

        await planner.Ok("POST", $"/api/strategy/initiatives/{initiative}/activities", Activity("نشاط يبقى مع مبادرته"));
        Assert.Equal(204, (await planner.Call("POST", $"/api/strategy/initiatives/{initiative}/delete", Scenario.O(("reason", "مبادرة اختبار انتهت")))).Status);
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM kpi.initiative_activities WHERE initiative_id = @id", ("id", Guid.Parse(initiative))));
    }
}
