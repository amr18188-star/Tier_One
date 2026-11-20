namespace Tier1.IntegrationTests;

/// <summary>Requirement 4-15 and BE-11: the in-app inbox, and resending a failed notification mail.</summary>
[Collection("app")]
public sealed class NotificationTests(Tier1App app)
{
    [Fact]
    public async Task A_submitted_form_reaches_the_reviewers_inbox_which_only_they_can_mark_read()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var link = $"/forms/{form["id"].Str()}";
        var college = await app.As("college");

        JsonNode? item = null;
        await EventOutboxTests.WaitAsync(async () =>
            (item = (await college.Get("/api/notifications"))!["items"]!.AsArray().FirstOrDefault(i => i!["link"].Str() == link)) is not null);
        Assert.Equal("dc.form", item!["kind"].Str());
        Assert.Contains(cycle.Name, item["title"].Str(), StringComparison.Ordinal);
        Assert.Null(item["readAt"].Str());
        var unread = (await college.Get("/api/notifications"))!["unread"].Num();

        // Someone else's item does not exist for them.
        var (status, _) = await (await app.As("planner")).Call("POST", $"/api/notifications/{item["id"].Str()}/read");
        Assert.Equal(404, status);

        (status, _) = await college.Call("POST", $"/api/notifications/{item["id"].Str()}/read");
        Assert.Equal(204, status);
        var after = (await college.Get("/api/notifications"))!;
        Assert.Equal(unread - 1, after["unread"].Num());
        Assert.NotNull(after["items"]!.AsArray().First(i => i!["id"].Str() == item["id"].Str())!["readAt"].Str());
    }

    [Fact]
    public async Task A_failed_notification_mail_is_resent_once_and_an_account_mail_is_not()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var link = $"/forms/{form["id"].Str()}";
        await EventOutboxTests.WaitAsync(async () =>
            await app.CountAsync("SELECT count(*) FROM notify.inbox WHERE link = @l", ("l", link)) > 0);
        var itemId = (Guid)(await app.ScalarAsync("SELECT id FROM notify.inbox WHERE link = @l LIMIT 1", ("l", link)))!;

        const string failed = "INSERT INTO adm.mail_outbox (kind, \"to\", subject, body_protected, created_at, next_attempt_at, attempts, failed_at, last_error, related_type, related_id) " +
            "VALUES (@k, 'old@tier1.local', @s, '', now(), now(), 8, now(), 'SmtpCommandException: mailbox unavailable', @t, @r) RETURNING id";
        var tag = Calls.Tag();
        var notification = (long)(await app.ScalarAsync(failed, ("k", "dc.form"), ("s", $"failed-{tag}"), ("t", "notification"), ("r", itemId)))!;
        var account = (long)(await app.ScalarAsync(failed, ("k", "portal.reset"), ("s", $"reset-{tag}"), ("t", "account"), ("r", Guid.NewGuid())))!;

        var (status, _) = await (await app.As("entry")).Call("GET", "/api/admin/mail?status=failed");
        Assert.Equal(403, status);

        var sys = await app.As("sysadmin");
        var rows = (await sys.Get("/api/admin/mail?status=failed")).Items().ToList();
        var row = rows.Single(r => r["id"].Num() == notification);
        Assert.True(row["resendable"].Truthy());
        Assert.DoesNotContain("old@", row["to"].Str(), StringComparison.Ordinal);
        Assert.False(rows.Single(r => r["id"].Num() == account)["resendable"].Truthy());

        var resent = await sys.Ok("POST", $"/api/admin/mail/{notification}/resend");
        Assert.Equal((decimal)notification, resent["resentFrom"].Num());
        Assert.Equal("pending", resent["status"].Str());
        // The resend goes to the reviewer's current address, not the one that failed.
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM adm.mail_outbox WHERE resent_from_id = @id AND \"to\" = 'old@tier1.local'", ("id", notification)));

        (status, var error) = await sys.Call("POST", $"/api/admin/mail/{notification}/resend");
        Assert.Equal((409, "already_resent"), (status, error.Code()));
        (status, error) = await sys.Call("POST", $"/api/admin/mail/{account}/resend");
        Assert.Equal((409, "not_resendable"), (status, error.Code()));
    }
}
