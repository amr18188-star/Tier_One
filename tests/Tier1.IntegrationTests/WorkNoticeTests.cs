using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>
/// System integration review 2026-11-24, G-03 and G-04: work handed to someone else reaches them in the inbox and by mail, and a step
/// whose own reviewer is missing still reaches the people who may approve past it.
/// </summary>
[Collection("app")]
public sealed class WorkNoticeTests(Tier1App app, ITestOutputHelper output)
{
    private Task<long> InboxAsync(string account, string kind, string like) =>
        app.CountAsync("SELECT count(*) FROM notify.inbox i JOIN iam.users u ON u.id = i.user_id WHERE u.email = @e AND i.kind = @k AND i.title LIKE @t",
            ("e", $"{account}@tier1.local"), ("k", kind), ("t", $"%{like}%"));

    private async Task<bool> ToldAsync(string account, string kind, string like)
    {
        try
        {
            await EventOutboxTests.WaitAsync(async () => await InboxAsync(account, kind, like) > 0, 20);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or Xunit.Sdk.XunitException)
        {
            return false;
        }
    }

    /// <summary>G-04: a reopen request reaches the higher committee and its decision reaches the requester; a proposed target reaches the committee.</summary>
    [Fact]
    public async Task G04_reopen_requests_decisions_and_proposed_targets_reach_the_next_party()
    {
        var t = new Scenario(output);
        var planner = await app.As("planner");
        var committee = await app.As("committee");

        t.Step("An approved form, then a reopen request and its decision");
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var id = form["id"].Str()!;
        foreach (var reviewer in new[] { "college", "dean", "tier1" })
        {
            var client = await app.As(reviewer);
            await client.Ok("POST", $"/api/forms/{id}/approve", Flow.Approve((await client.Get($"/api/forms/{id}"))!));
        }
        var unit = (await planner.Get($"/api/forms/{id}"))!["unitName"].Str()!;
        await planner.Ok("POST", $"/api/forms/{id}/reopen", Scenario.O(("version", (await planner.Get($"/api/forms/{id}"))!["version"]), ("note", "تصحيح قيمة بعد الاعتماد")));
        t.Check(await ToldAsync("committee", "dc.reopen", $"طلب إعادة فتح نموذج {unit}"), "The higher committee is told of the reopen request");
        t.Check(await InboxAsync("planner", "dc.reopen", "طلب إعادة فتح") == 0, "The requester is not told of their own request");
        await committee.Ok("POST", $"/api/forms/{id}/reopen/approve", Scenario.O(("decisionRef", "محضر 3/2027"), ("note", null)));
        t.Check(await ToldAsync("planner", "dc.reopen", $"اعتُمد طلبك إعادة فتح نموذج {unit}"), "The requester is told of the decision");

        t.Step("A proposed target");
        var code = (await planner.Get("/api/kpi/indicators")).Items().First(i => i["status"].Str() == "Approved")["code"].Str()!;
        var cs = (await planner.Get("/api/org/units")).Items().Single(u => u["code"].Str() == "CS");
        var year = 2028 + Random.Shared.Next(0, 3);
        var existing = (await planner.Get($"/api/kpi/indicators/{code}/targets"))!["colleges"].Items()
            .FirstOrDefault(x => x["unitId"].Str() == cs["id"].Str() && x["year"].Num() == year && x["kind"].Str() == "Target");
        if (existing is null || existing["status"].Str() != "Approved")
        {
            await planner.Ok("PUT", $"/api/kpi/indicators/{code}/targets", Scenario.O(("unitId", cs["id"]), ("year", year), ("kind", "Target"), ("value", 61),
                ("source", "اختبار الإشعارات"), ("version", existing?["version"])));
            t.Check(await ToldAsync("committee", "kpi.target", $"مستهدف مقترح للمؤشر {code}"), "The higher committee is told of the proposed target");
        }
        t.Finish();
    }

    /// <summary>
    /// G-03: a department form whose department has no representative. Before the cycle opens the preview names the missing
    /// reviewer; after the form is sent, the college committee (who may approve past the step) is told, which it was not before.
    /// </summary>
    [Fact]
    public async Task G03_a_department_without_a_representative_is_named_before_opening_and_its_form_reaches_the_college_committee()
    {
        var t = new Scenario(output);
        var planner = await app.As("planner");
        var president = await app.As("president");
        var sa = await app.As("sa1");
        var repId = (await sa.Get("/api/iam/users?q=deptrep@tier1.local")).Items().Single()["id"].Str()!;
        var assignment = (await sa.Get($"/api/iam/users/{repId}"))!["assignments"].Items().Single(a => a["roleCode"].Str() == "DREP" && a["active"].Truthy());
        var scope = assignment["scopeValues"]!.DeepClone();
        await president.Ok("POST", $"/api/iam/assignments/{assignment["id"].Str()}/revoke", Scenario.O(("reason", "اختبار قسم بلا ممثل")), expect: 204);
        try
        {
            t.Step("The open preview names the department and its missing reviewer");
            var label = $"it-{Calls.Tag()}";
            var draft = await planner.Ok("POST", "/api/cycles", Scenario.O(("name", $"دورة قسم بلا ممثل {label}"), ("periodLabel", label), ("targetYear", 2027),
                ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", true), ("version", null)));
            var preview = (await planner.Get($"/api/cycles/{draft["id"].Str()}/preview"))!;
            var civil = (await planner.Get("/api/org/units")).Items().Single(u => u["code"].Str() == "ENG-CIV");
            var line = preview["unheld"].Items().Select(x => x.Str()).FirstOrDefault(x => x!.StartsWith(civil["nameAr"].Str()!, StringComparison.Ordinal));
            t.Check(line is not null && line.Contains("ممثل Tier 1 بالقسم (يمر باعتماد مباشر)", StringComparison.Ordinal),
                $"The preview names the department and the step a direct approval must pass: {line}");
            t.Check(preview["unheldCount"].Num() >= 1, $"It counts the units ({preview["unheldCount"].Num()})");

            t.Step("The department's form reaches the college committee");
            await planner.Ok("POST", $"/api/cycles/{draft["id"].Str()}/open");
            var cycle = new Flow.Cycle(draft["id"].Str()!, draft["name"].Str()!);
            var entry = await app.As("entry");
            var form = await entry.Fill(await Flow.FormAsync(app, cycle, "ENG-CIV", "entry"));
            await entry.Ok("POST", $"/api/forms/{form["id"].Str()}/submit", Scenario.O(("version", form["version"])));
            var link = $"/forms/{form["id"].Str()}";
            await EventOutboxTests.WaitAsync(async () => await app.CountAsync(
                "SELECT count(*) FROM notify.inbox i JOIN iam.users u ON u.id = i.user_id WHERE u.email = 'college@tier1.local' AND i.link = @l", ("l", link)) > 0, 20);
            t.Check(true, "The college committee, who may approve it directly to the dean, is told");
            var college = await app.As("college");
            var seen = (await college.Get($"/api{link}"))!;
            t.Check(seen["actions"]!["directDepartmentApproval"].Truthy(), "It can approve it directly to the dean");

            t.Step("At the dean's step with no dean, the plan manager sees a form nobody can move");
            await college.Ok("POST", $"/api{link}/approve", Scenario.O(("note", "لا ممثل Tier 1 لهذا القسم"), ("version", seen["version"]), ("direct", true)));
            var deanId = (await sa.Get("/api/iam/users?q=dean@tier1.local")).Items().Single(u => u["email"].Str() == "dean@tier1.local")["id"].Str()!;
            var deanRole = (await sa.Get($"/api/iam/users/{deanId}"))!["assignments"].Items().Single(a => a["roleCode"].Str() == "DEAN" && a["active"].Truthy());
            await president.Ok("POST", $"/api/iam/assignments/{deanRole["id"].Str()}/revoke", Scenario.O(("reason", "اختبار خطوة بلا صاحب")), expect: 204);
            try
            {
                var task = (await planner.Get("/api/home"))!["tasks"].Items().FirstOrDefault(x => x["key"].Str() == "forms_unheld");
                t.Check(task is not null && task["count"].Num() >= 1, $"G-03: the home page lists forms at a step nobody holds ({task?["count"].Num()})");
            }
            finally
            {
                await president.Ok("POST", $"/api/iam/users/{deanId}/roles", Scenario.O(("roleId", deanRole["roleId"]), ("scopeValues", deanRole["scopeValues"]!.DeepClone()),
                    ("validFrom", null), ("validTo", null), ("reason", "إعادة العميد بعد الاختبار")));
            }
        }
        finally
        {
            await president.Ok("POST", $"/api/iam/users/{repId}/roles", Scenario.O(("roleId", assignment["roleId"]), ("scopeValues", scope), ("validFrom", null), ("validTo", null),
                ("reason", "إعادة ممثل القسم بعد الاختبار")));
        }
        t.Finish();
    }
}
