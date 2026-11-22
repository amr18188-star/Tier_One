using Xunit.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>
/// The form workflow against a real database: concurrent saves, the approval matrix across colleges (D1),
/// refused transitions and stale versions (409), and a move with its notification in one commit.
/// </summary>
[Collection("app")]
public sealed class FormFlowTests(Tier1App app, ITestOutputHelper output)
{
    [Fact]
    public async Task Two_saves_at_once_in_the_same_form_both_land()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var entry = await app.As("entry");
        var form = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var id = form["id"].Str();
        var items = form.ItemsOf().ToList();
        Assert.True(items.Count >= 3, "The Engineering form needs at least three indicators for this test.");

        // Two different indicators at the same moment: the form row is touched by both, and neither save is lost.
        var both = await Task.WhenAll(
            entry.Call("PUT", $"/api/forms/{id}/values", Calls.InputFor(items[0])),
            entry.Call("PUT", $"/api/forms/{id}/values", Calls.InputFor(items[1])));
        Assert.All(both, r => Assert.Equal(200, r.Status));
        var after = (await entry.Get($"/api/forms/{id}"))!.ItemsOf().ToDictionary(i => i["indicatorId"].Str()!);
        Assert.True(after[items[0]["indicatorId"].Str()!]["value"].Truthy(), "The first value was saved");
        Assert.True(after[items[1]["indicatorId"].Str()!]["value"].Truthy(), "The second value was saved");

        // The same indicator twice from the same version: one wins, the other is told the value changed.
        var first = await entry.Ok("PUT", $"/api/forms/{id}/values", Calls.InputFor(items[2]));
        var edit = Calls.InputFor(items[2]);
        edit["version"] = first["value"]!["version"].Copy();
        var race = await Task.WhenAll(
            entry.Call("PUT", $"/api/forms/{id}/values", Scenario.With(edit, ("explanation", "التعديل الأول"))),
            entry.Call("PUT", $"/api/forms/{id}/values", Scenario.With(edit, ("explanation", "التعديل الثاني"))));
        Assert.Equal([200, 409], race.Select(r => r.Status).Order());

        // BE-2: a save that saw no value (version null) does not overwrite a value saved meanwhile.
        var blind = Calls.InputFor(items[2]);
        blind["version"] = null;
        var (status, error) = await entry.Call("PUT", $"/api/forms/{id}/values", blind);
        Assert.Equal((409, "conflict"), (status, error.Code()));
    }

    [Fact]
    public async Task Only_the_reviewers_of_the_form_s_own_college_move_it_along()
    {
        var t = new Scenario(output);
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var id = form["id"].Str();
        t.Check(form["step"].Str() == "CollegeCommittee", "Submitted to the Engineering college committee");

        t.Step("College committee step");
        foreach (var other in new[] { "college.cs", "college.com", "dean.cs" })
            t.Check((await (await app.As(other)).Call("POST", $"/api/forms/{id}/approve", Flow.Approve(form))).Status == 404, $"{other} of another college does not even see the form (404)");
        foreach (var noReviewer in new[] { "entry", "viewer" })
            t.Check((await (await app.As(noReviewer)).Call("POST", $"/api/forms/{id}/approve", Flow.Approve(form))).Status == 403, $"{noReviewer} holds no review action (403)");
        form = await (await app.As("college")).Ok("POST", $"/api/forms/{id}/approve", Flow.Approve(form));
        t.Check(form["step"].Str() == "Dean", "The Engineering committee approved; the form is at the dean");

        t.Step("Dean step");
        foreach (var other in new[] { "dean.cs", "dean.com", "college.cs" })
            t.Check((await (await app.As(other)).Call("POST", $"/api/forms/{id}/approve", Flow.Approve(form))).Status == 404, $"{other} of another college does not see the form (404)");
        var (status, _) = await (await app.As("college")).Call("POST", $"/api/forms/{id}/approve", Flow.Approve(form));
        t.Check(status == 403, "The committee cannot also approve the dean's step (403)");
        form = await (await app.As("dean")).Ok("POST", $"/api/forms/{id}/approve", Flow.Approve(form));
        t.Check(form["step"].Str() == "Quality", "The Engineering dean approved; the form is at the university Tier 1 committee");

        t.Step("University Tier 1 committee step");
        // 2026-11-23: the final approval moved from central quality (QA) to the university Tier 1 committee (T1O).
        t.Check((await (await app.As("quality")).Call("POST", $"/api/forms/{id}/approve", Flow.Approve(form))).Status == 403, "Central quality no longer gives the final approval (403)");
        form = await (await app.As("tier1")).Ok("POST", $"/api/forms/{id}/approve", Flow.Approve(form));
        t.Check(form["status"].Str() == "Approved", "The university Tier 1 committee approved the form");
        t.Finish();
    }

    [Fact]
    public async Task A_reviewer_from_the_previous_round_sees_the_resent_form_awaiting_them_CR4()
    {
        var t = new Scenario(output);
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var id = form["id"].Str();
        var college = await app.As("college");
        form = await college.Ok("POST", $"/api/forms/{id}/approve", Flow.Approve(form));
        form = await (await app.As("dean")).Ok("POST", $"/api/forms/{id}/return", Scenario.O(("note", "صحح القيم قبل الاعتماد"), ("items", new JsonArray()), ("version", form["version"])));
        t.Check(form["status"].Str() == "Returned", "The dean returned the form after the committee approved it");

        var entry = await app.As("entry");
        form = (await entry.Get($"/api/forms/{id}"))!;
        form = await entry.Ok("POST", $"/api/forms/{id}/submit", Scenario.O(("version", form["version"])));
        t.Check(form["step"].Str() == "CollegeCommittee", "Resent, the form is back at the committee");

        // One rule for the list, the page and the server: last round's approval does not bar the committee this round.
        var row = (await college.Get($"/api/forms?cycleId={cycle.Id}&awaitingMe=true")).Items().SingleOrDefault(r => r["id"].Str() == id);
        t.Check(row is not null, "The resent form is in the committee's \"awaiting me\" list");
        form = (await college.Get($"/api/forms/{id}"))!;
        t.Check(form["actions"]!["canReview"].Truthy(), "The form page offers the committee its decision");
        form = await college.Ok("POST", $"/api/forms/{id}/approve", Flow.Approve(form));
        t.Check(form["step"].Str() == "Dean", "The committee approved the new round");
        t.Finish();
    }

    [Fact]
    public async Task Refused_transitions_and_stale_versions_are_conflicts_and_leave_nothing_behind()
    {
        var t = new Scenario(output);
        var cycle = await Flow.OpenCycleAsync(app);
        var entry = await app.As("entry");
        var college = await app.As("college");

        var draft = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var (status, error) = await college.Call("POST", $"/api/forms/{draft["id"].Str()}/approve", Flow.Approve(draft));
        t.Check(status == 409 && error.Code() == "not_in_review", $"Approving a form still at entry is a conflict (409 not_in_review), got {status} {error.Code()}");

        var mailBefore = await MailFor(cycle);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var id = form["id"].Str()!;
        t.Check(await app.CountAsync("SELECT count(*) FROM \"int\".outbox_events WHERE aggregate_id = @id AND type = 'form.moved'", ("id", Guid.Parse(id))) == 1,
            "Submitting saved its form.moved event in the same commit as the move");
        await EventOutboxTests.WaitAsync(async () => await MailFor(cycle) > mailBefore);

        (status, error) = await entry.Call("POST", $"/api/forms/{id}/submit");
        t.Check(status == 409 && error.Code() == "form_locked", $"Submitting twice is a conflict (409 form_locked), got {status} {error?.ToJsonString()}");

        (status, error) = await college.Call("POST", $"/api/forms/{id}/approve", Scenario.O(("note", null)));
        t.Check(status == 428 && error.Code() == "version_required", $"A transition without the form's version is refused (428), got {status} {error.Code()}");
        (status, error) = await college.Call("POST", $"/api/forms/{id}/return", Scenario.O(("note", "ملاحظة بلا إصدار"), ("items", new JsonArray())));
        t.Check(status == 428, $"Returning without the version is refused too (428), got {status}");

        var stale = Scenario.With(Flow.Approve(form), ("version", form["version"].Num() - 1));
        // The submission's own notification is written after its commit: let it land before counting.
        await EventOutboxTests.WaitAsync(async () =>
            await app.CountAsync("SELECT count(*) FROM \"int\".outbox_events WHERE aggregate_id = @id AND processed_at IS NULL", ("id", Guid.Parse(id!))) == 0);
        var mail = await MailFor(cycle);
        var events = await EventsFor(id);
        (status, error) = await college.Call("POST", $"/api/forms/{id}/approve", stale);
        t.Check(status == 409 && error.Code() == "conflict", $"Approving from an old version is a conflict (409), got {status} {error.Code()}");
        t.Check(await MailFor(cycle) == mail && await EventsFor(id) == events, "The refused approval left no event and no notification");
        var current = (await college.Get($"/api/forms/{id}"))!;
        t.Check(current["step"].Str() == "CollegeCommittee", "The form did not move");

        var moved = await college.Ok("POST", $"/api/forms/{id}/approve", Flow.Approve(current));
        t.Check(moved["step"].Str() == "Dean" && await EventsFor(id) == events + 1, "The approval from the current version moved the form, with one event");
        await EventOutboxTests.WaitAsync(async () => await MailFor(cycle) > mail);
        t.Finish();
    }

    private Task<long> MailFor(Flow.Cycle cycle) =>
        app.CountAsync("SELECT count(*) FROM adm.mail_outbox WHERE subject LIKE '%' || @name || '%'", ("name", cycle.Name));

    private Task<long> EventsFor(string formId) =>
        app.CountAsync("SELECT count(*) FROM dc.submission_events WHERE submission_id = @id", ("id", Guid.Parse(formId)));
}
