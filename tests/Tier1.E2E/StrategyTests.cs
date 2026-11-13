using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// The strategy tree: add, edit and delete a pillar, standard, initiative and indicator; a target plan with a start and an achievement year;
/// protection of what others depend on; permissions. It cleans up what it creates, so it repeats on one database.
/// </summary>
public sealed class StrategyTests(ITestOutputHelper output)
{
    private static JsonNode NodeOf(JsonNode tree, string standardId) =>
        tree["pillars"].Items().SelectMany(p => p["standards"].Items()).Where(s => s["id"].Str() == standardId).SelectMany(s => s["indicators"].Items()).First();

    [E2EFact]
    public async Task Tree_plans_and_delete_protection()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var committee = await Env.As("committee");
        var viewer = await Env.As("viewer");
        var tag = Calls.Tag(5);

        t.Step("1. The tree and permissions");
        var tree = (await planner.Get("/api/strategy/tree"))!;
        t.Check(tree["pillars"].Items().Count() >= 6 && tree["years"]![0].Num() == tree["baselineYear"].Num() + 1 && tree["years"].Items().Count() == 5, "The tree with its pillars and five plan years");
        t.Check(tree["actions"]!["canAdd"].Truthy() && tree["actions"]!["canDelete"].Truthy(), "The plan manager can add and delete");
        var v = (await viewer.Get("/api/strategy/tree"))!;
        t.Check(!v["actions"]!["canEdit"].Truthy() && !v["actions"]!["canEditTargets"].Truthy(), "The viewer sees the tree without editing");
        var (status, err) = await viewer.Call("POST", "/api/strategy/pillars", Scenario.O(("nameAr", $"ركيزة {tag}")));
        t.Check(status == 403, "The viewer cannot add a pillar");

        t.Step("2. Pillar, standard and initiative");
        var pillar = await planner.Ok("POST", "/api/strategy/pillars", Scenario.O(("nameAr", $"ركيزة تجريبية {tag}"), ("description", "شرح الركيزة")));
        t.Check(pillar["code"].Str()!.StartsWith('P'), $"Pillar {pillar["code"].Str()} created with an automatic code");
        (status, err) = await planner.Call("POST", "/api/strategy/pillars", Scenario.O(("nameAr", $"ركيزة تجريبية {tag}")));
        t.Check(status == 422 && err.Code() == "duplicate_name", "A pillar name is not repeated");
        pillar = await planner.Ok("PUT", $"/api/strategy/pillars/{pillar["id"].Str()}", Scenario.O(("nameAr", pillar["nameAr"]), ("description", "شرح معدل"), ("version", pillar["version"])));
        t.Check(pillar["description"].Str() == "شرح معدل", "The pillar description was edited");
        var std = await planner.Ok("POST", "/api/strategy/standards", Scenario.O(("pillarId", pillar["id"]), ("nameAr", $"معيار تجريبي {tag}"), ("description", "ما يقيسه")));
        t.Check(std["code"].Str()!.StartsWith('S'), $"Standard {std["code"].Str()} created");
        (status, err) = await planner.Call("POST", "/api/strategy/initiatives", Scenario.O(("standardId", std["id"]), ("nameAr", "مبادرة"), ("startYear", 2030), ("endYear", 2028), ("status", "Planned"), ("indicatorCodes", new JsonArray())));
        t.Check(status == 400 && err.Code() == "plan_order", "An initiative cannot end before it starts");
        var ini = await planner.Ok("POST", "/api/strategy/initiatives", Scenario.O(("standardId", std["id"]), ("nameAr", $"مبادرة {tag}"), ("description", "أنشطة"), ("startYear", 2027), ("endYear", 2029), ("status", "InProgress"), ("indicatorCodes", new JsonArray())));
        t.Check(ini["code"].Str() == $"{std["code"].Str()}-M01", $"Initiative {ini["code"].Str()} created");

        t.Step("3. An indicator and its target plan");
        var created = await planner.Ok("POST", "/api/kpi/indicators", Scenario.O(("standardCode", std["code"]), ("nameAr", $"مؤشر تجريبي {tag}"), ("unit", "%"), ("level", "College")));
        var code = created["code"].Str()!;
        var node = NodeOf((await planner.Get("/api/strategy/tree"))!, std["id"].Str()!);
        (status, err) = await planner.Call("PUT", $"/api/kpi/indicators/{code}/plan", Scenario.O(("startYear", 2029), ("endYear", 2031), ("values", new JsonArray(Scenario.O(("year", 2028), ("value", 50)))), ("version", node["version"])));
        t.Check(status == 400 && err.Code() == "before_start", "No target before the indicator's start year");
        (status, _) = await planner.Call("PUT", $"/api/kpi/indicators/{code}/plan", Scenario.O(("startYear", 2029), ("endYear", 2031), ("note", "يبدأ بعد سنتين"),
            ("values", new JsonArray(Scenario.O(("year", 2029), ("value", 40)), Scenario.O(("year", 2031), ("value", 70)))), ("version", node["version"])));
        t.Check(status == 204, "Saved the plan of an indicator that starts in two years and is achieved at the end of the plan");
        node = NodeOf((await planner.Get("/api/strategy/tree"))!, std["id"].Str()!);
        t.Check(node["planStartYear"].Num() == 2029 && node["planEndYear"].Num() == 2031 && node["planNote"].Str() == "يبدأ بعد سنتين", "The start and achievement years and the note are saved");
        t.Check(node["targets"].Items().ToDictionary(x => (int)x["year"].Num()!, x => x["value"].Num()).SequenceEqual(new Dictionary<int, decimal?> { [2029] = 40, [2031] = 70 }), "The yearly values are saved");
        (status, _) = await planner.Call("PUT", $"/api/kpi/indicators/{code}/plan", Scenario.O(("startYear", 2029), ("endYear", 2031), ("values", new JsonArray(Scenario.O(("year", 2029), ("value", 120)))), ("version", node["version"])));
        t.Check(status == 400, "A target percentage over 100 is refused");
        var target = node["targets"].Items().First(x => x["year"].Num() == 2031);
        await committee.Ok("POST", $"/api/kpi/targets/{target["id"].Str()}/approve", Scenario.O(("decisionRef", "محضر تجريبي")));
        node = NodeOf((await planner.Get("/api/strategy/tree"))!, std["id"].Str()!);
        (status, _) = await planner.Call("PUT", $"/api/kpi/indicators/{code}/plan", Scenario.O(("startYear", 2029), ("endYear", 2031), ("values", new JsonArray(Scenario.O(("year", 2031), ("value", 75)))), ("version", node["version"])));
        t.Check(status == 403, "Editing an approved target needs the approval permission");
        ini = await planner.Ok("PUT", $"/api/strategy/initiatives/{ini["id"].Str()}", Scenario.O(("standardId", std["id"]), ("nameAr", ini["nameAr"]), ("startYear", 2027), ("endYear", 2029), ("status", "InProgress"), ("indicatorCodes", new JsonArray((JsonNode)code)), ("version", ini["version"])));
        t.Check(ini["indicatorCodes"].Items().Select(c => c.Str()).SequenceEqual([code]), "The initiative was linked to the indicator");

        t.Step("4. Deleting protects what others depend on");
        (status, err) = await planner.Call("POST", $"/api/strategy/standards/{std["id"].Str()}/delete", Scenario.O(("reason", "تنظيف الاختبار")));
        t.Check(status == 409 && err.Code() == "standard_in_use", "A standard with an indicator and an initiative cannot be deleted");
        (status, err) = await planner.Call("POST", $"/api/strategy/pillars/{pillar["id"].Str()}/delete", Scenario.O(("reason", "تنظيف الاختبار")));
        t.Check(status == 409 && err.Code() == "pillar_in_use", "A pillar with a standard cannot be deleted");
        (status, _) = await planner.Call("POST", $"/api/kpi/indicators/{code}/delete", Scenario.O(("reason", "قصير")));
        t.Check(status == 400, "Deleting asks for a clear reason");
        await planner.Ok("POST", $"/api/kpi/indicators/{code}/delete", Scenario.O(("reason", "تنظيف الاختبار")), 204);
        tree = (await planner.Get("/api/strategy/tree"))!;
        var left = tree["pillars"].Items().SelectMany(p => p["standards"].Items()).SelectMany(s => s["initiatives"].Items()).First(m => m["id"].Str() == ini["id"].Str());
        t.Check(!left["indicatorCodes"].Truthy(), "Deleting the indicator removed it from the initiative");
        await planner.Ok("POST", $"/api/strategy/initiatives/{ini["id"].Str()}/delete", Scenario.O(("reason", "تنظيف الاختبار")), 204);
        await planner.Ok("POST", $"/api/strategy/standards/{std["id"].Str()}/delete", Scenario.O(("reason", "تنظيف الاختبار")), 204);
        await planner.Ok("POST", $"/api/strategy/pillars/{pillar["id"].Str()}/delete", Scenario.O(("reason", "تنظيف الاختبار")), 204);
        tree = (await planner.Get("/api/strategy/tree"))!;
        t.Check(tree["pillars"].Items().All(p => p["id"].Str() != pillar["id"].Str()), "The pillar was deleted once emptied");
        t.Finish();
    }
}
