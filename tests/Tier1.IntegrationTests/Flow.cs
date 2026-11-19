namespace Tier1.IntegrationTests;

/// <summary>Shared steps: a trial cycle of its own for each test, and a unit's form in it.</summary>
internal static class Flow
{
    public sealed record Cycle(string Id, string Name);

    public static async Task<Cycle> OpenCycleAsync(Tier1App app)
    {
        var planner = await app.As("planner");
        var label = $"it-{Calls.Tag()}";
        var name = $"دورة اختبار التكامل {label}";
        var body = Scenario.O(("name", name), ("periodLabel", label), ("targetYear", 2027), ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", true), ("version", null));
        var cycle = await planner.Ok("POST", "/api/cycles", body);
        await planner.Ok("POST", $"/api/cycles/{cycle["id"].Str()}/open");
        return new Cycle(cycle["id"].Str()!, name);
    }

    public static async Task<string> UnitIdAsync(Tier1App app, string code)
    {
        var planner = await app.As("planner");
        return (await planner.Get("/api/org/units")).Items().Single(u => u["code"].Str() == code)["id"].Str()!;
    }

    /// <summary>The unit's form in the cycle, as the given account sees it.</summary>
    public static async Task<JsonNode> FormAsync(Tier1App app, Cycle cycle, string unitCode, string account)
    {
        var unitId = await UnitIdAsync(app, unitCode);
        var planner = await app.As("planner");
        var row = (await planner.Get($"/api/forms?cycleId={cycle.Id}")).Items().Single(f => f["unitId"].Str() == unitId);
        return (await (await app.As(account)).Get($"/api/forms/{row["id"].Str()}"))!;
    }

    /// <summary>The Engineering form filled by its data entry user and sent to the college committee.</summary>
    public static async Task<JsonNode> SubmittedEngineeringFormAsync(Tier1App app, Cycle cycle)
    {
        var entry = await app.As("entry");
        var form = await entry.Fill(await FormAsync(app, cycle, "ENG", "entry"));
        return await entry.Ok("POST", $"/api/forms/{form["id"].Str()}/submit", Scenario.O(("version", form["version"])));
    }

    public static JsonObject Approve(JsonNode form, string? note = null) => Scenario.O(("note", note), ("version", form["version"]), ("direct", false));
}
