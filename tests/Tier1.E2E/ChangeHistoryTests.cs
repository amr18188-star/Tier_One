using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// D8 (ADR-0008): one authorised person may change the official university value or an approved target, and nothing is lost:
/// the old and new values, who, when and why stay on record, also after an approved target is deleted.
/// </summary>
public sealed class ChangeHistoryTests(ITestOutputHelper output)
{
    [E2EFact]
    public async Task The_official_value_keeps_every_change_with_who_when_and_why()
    {
        var t = new Scenario(output);
        var officer = await Env.As("tier1");

        t.Step("1. The Tier 1 officer transfers a value, then adjusts it with a reason");
        var review = (await officer.Get("/api/review"))!;
        var cycleId = review["cycle"]!["id"].Str();
        var row = review["rows"].Items().FirstOrDefault(r => r["computed"].Num() is not null);
        Assert.True(row is not null, "No indicator with a computed university value in the review; reset the data.");
        var indicatorId = row!["indicatorId"].Str();
        var before = row["history"].Items().Count();
        var computed = row["computed"].Num()!.Value;
        var original = row["official"]?["value"].Num();
        JsonNode Transfer(decimal value, string note, JsonNode? version) =>
            Scenario.O(("cycleId", cycleId), ("indicatorId", indicatorId), ("value", value), ("note", note), ("version", version));
        var first = await officer.Ok("POST", "/api/review/transfer", Transfer(computed + 1, "تعديل أول لاختبار السجل", row["official"]?["version"]));
        await officer.Ok("POST", "/api/review/transfer", Transfer(computed + 2, "تعديل ثان لاختبار السجل", first["official"]?["version"]));

        t.Step("2. The history shows both changes, newest first, with the previous value");
        var after = (await officer.Get($"/api/review?cycleId={cycleId}"))!["rows"].Items().First(r => r["indicatorId"].Str() == indicatorId);
        var history = after["history"].Items().ToList();
        t.Check(history.Count == before + 2, $"Two new history entries ({before} -> {history.Count})");
        t.Check(history[0]["oldValue"].Num() == computed + 1 && history[0]["newValue"].Num() == computed + 2 && history[0]["reason"].Str() == "تعديل ثان لاختبار السجل",
            "The newest entry: previous value, new value and reason");
        t.Check(history[1]["oldValue"].Num() == original && history[1]["newValue"].Num() == computed + 1, "The earlier entry keeps the value it replaced");
        t.Check(history.Take(2).All(h => h["changedByName"].Str() is { Length: > 0 } && h["changedAt"].Str() is { Length: > 0 }), "Each entry says who and when");

        // Put the official value back as it was (or to the computed one), which is itself recorded.
        await officer.Ok("POST", "/api/review/transfer", Transfer(original ?? computed, "إعادة القيمة بعد اختبار السجل", after["official"]?["version"]));
        t.Finish();
    }

    [E2EFact]
    public async Task Deleting_an_approved_target_keeps_its_history()
    {
        var t = new Scenario(output);
        var planner = await Env.As("planner");
        var committee = await Env.As("committee");
        var admin = await Env.As("sa1");
        var units = await Env.UnitsAsync(planner);
        var cs = units["CS"];
        const string code = "3.01";

        t.Step("1. A Computers college target is proposed, approved, then amended with a decision");
        var taken = (await planner.Get($"/api/kpi/indicators/{code}/targets"))!["colleges"].Items()
            .Where(r => r["unitId"].Str() == cs["id"].Str()).Select(r => (int)r["year"].Num()!).ToHashSet();
        var year = Enumerable.Range(2027, 5).First(y => !taken.Contains(y));
        var target = await planner.Ok("PUT", $"/api/kpi/indicators/{code}/targets", Scenario.O(("unitId", cs["id"]), ("year", year), ("kind", "Target"), ("value", 5)));
        target = await committee.Ok("POST", $"/api/kpi/targets/{target["id"].Str()}/approve", Scenario.O(("decisionRef", "محضر 21")));
        target = await admin.Ok("PUT", $"/api/kpi/indicators/{code}/targets", Scenario.O(("unitId", cs["id"]), ("year", year), ("kind", "Target"), ("value", 6),
            ("reason", "تعديل الحصة بعد مراجعة الطاقة"), ("decisionRef", "محضر 22"), ("version", target["version"])));

        t.Step("2. It is deleted by decision, and its history stays");
        await admin.Ok("POST", $"/api/kpi/targets/{target["id"].Str()}/delete", Scenario.O(("reason", "إلغاء المستهدف بقرار"), ("decisionRef", "محضر 23")), expect: 204);
        var revisions = (await planner.Get($"/api/kpi/indicators/{code}/targets"))!["revisions"].Items()
            .Where(r => r["unitName"].Str() == cs["nameAr"].Str() && r["year"].Num() == year).ToList();
        t.Check(revisions.Any(r => r["action"].Str() == "Revised" && r["oldValue"].Num() == 5 && r["newValue"].Num() == 6 && r["decisionRef"].Str() == "محضر 22"),
            "The amendment is still on record after deletion (5 -> 6, decision 22)");
        t.Check(revisions.Any(r => r["action"].Str() == "Deleted" && r["oldValue"].Num() == 6 && r["reason"].Str() == "إلغاء المستهدف بقرار" && r["decisionRef"].Str() == "محضر 23"),
            "The deletion is on record with the last value, reason and decision");
        t.Check(revisions.Count >= 2 && revisions.All(r => r["changedByName"].Str() is { Length: > 0 }), $"Each entry names who made it ({revisions.Count} entries)");
        t.Finish();
    }
}
