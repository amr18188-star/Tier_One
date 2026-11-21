using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tier1.Infrastructure.Persistence;

namespace Tier1.IntegrationTests;

/// <summary>
/// BE-8 (ADR-0018): a POST sent with an Idempotency-Key runs once. A retry with the same key and request gets the first answer
/// back; the same key with another request, or while the first still runs, is refused; a refused request frees its key.
/// </summary>
[Collection("app")]
public sealed class IdempotencyTests(Tier1App app)
{
    private static JsonObject Activity(string title) =>
        Scenario.O(("titleAr", title), ("description", null), ("ownerUnitId", null), ("startDate", "2026-09-01"), ("endDate", "2026-12-31"),
            ("status", "Planned"), ("progress", 0), ("sortOrder", null), ("version", null));

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static Dictionary<string, string> Key(string key) => new() { ["Idempotency-Key"] = key };

    private async Task<(DemoClient Planner, string Path)> ActivitiesAsync()
    {
        var planner = await app.As("planner");
        var standard = (await planner.Get("/api/strategy/tree"))!["pillars"].Items().SelectMany(p => p["standards"].Items()).First();
        var initiative = await planner.Ok("POST", "/api/strategy/initiatives", Scenario.O(("standardId", standard["id"]), ("nameAr", $"مبادرة اختبار {Calls.Tag()}"),
            ("description", null), ("expectedOutcome", null), ("ownerUnitId", null), ("startYear", 2027), ("endYear", 2028), ("status", "Planned"),
            ("indicatorCodes", new JsonArray()), ("version", null)));
        return (planner, $"/api/strategy/initiatives/{initiative["id"].Str()}/activities");
    }

    private Task<long> ActivitiesTitled(string title) =>
        app.CountAsync("SELECT count(*) FROM kpi.initiative_activities WHERE title_ar = @t", ("t", title));

    /// <summary>AC-I1: the retry of a create whose answer was lost gets the same answer, and nothing is created twice.</summary>
    [Fact]
    public async Task A_retry_with_the_same_key_gets_the_first_answer_and_creates_nothing()
    {
        var (planner, path) = await ActivitiesAsync();
        var title = $"نشاط مرة واحدة {Calls.Tag()}";
        var key = Guid.NewGuid().ToString();

        var first = await planner.PostWithHeadersAsync(path, Json(Activity(title)), Key(key));
        var retry = await planner.PostWithHeadersAsync(path, Json(Activity(title)), Key(key));

        Assert.Equal((200, 200), (first.Status, retry.Status));
        Assert.Equal(first.Body!["id"].Str(), retry.Body!["id"].Str());
        Assert.False(first.Headers.ContainsKey("Idempotent-Replayed"));
        Assert.Equal("true", retry.Headers["Idempotent-Replayed"]);
        Assert.Equal(1, await ActivitiesTitled(title));

        // Without a key nothing changes: two sends are two creates, as before (the web client always sends a key).
        await planner.Ok("POST", path, Activity(title));
        Assert.Equal(2, await ActivitiesTitled(title));
    }

    /// <summary>AC-I2: the same key with another request is refused, and the other request does not run.</summary>
    [Fact]
    public async Task The_same_key_with_another_request_is_refused()
    {
        var (planner, path) = await ActivitiesAsync();
        var key = Guid.NewGuid().ToString();
        var other = $"نشاط مختلف {Calls.Tag()}";

        Assert.Equal(200, (await planner.PostWithHeadersAsync(path, Json(Activity($"نشاط أول {Calls.Tag()}")), Key(key))).Status);
        var (status, error, _) = await planner.PostWithHeadersAsync(path, Json(Activity(other)), Key(key));

        Assert.Equal((422, "idempotency_key_reused"), (status, error.Code()));
        Assert.Equal(0, await ActivitiesTitled(other));
    }

    /// <summary>AC-I3: a refused request frees its key, so the corrected request runs with it.</summary>
    [Fact]
    public async Task A_refused_request_frees_its_key_for_the_corrected_one()
    {
        var (planner, path) = await ActivitiesAsync();
        var key = Guid.NewGuid().ToString();
        var title = $"نشاط بعد التصحيح {Calls.Tag()}";

        var (refused, error, _) = await planner.PostWithHeadersAsync(path, Json(Activity("  ")), Key(key));
        Assert.Equal((400, "titleAr"), (refused, error!["errors"]!.AsArray().First()!["field"].Str()));
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM iam.idempotency_keys WHERE key = @k", ("k", key)));

        Assert.Equal(200, (await planner.PostWithHeadersAsync(path, Json(Activity(title)), Key(key))).Status);
        Assert.Equal(1, await ActivitiesTitled(title));
    }

    /// <summary>AC-I4: while the first request's outcome is not recorded (still running, or the server stopped), a retry is refused, not run.</summary>
    [Fact]
    public async Task A_retry_while_the_outcome_is_unknown_is_refused()
    {
        var (planner, path) = await ActivitiesAsync();
        var key = Guid.NewGuid().ToString();
        var title = $"نشاط نتيجته غير معروفة {Calls.Tag()}";
        Assert.Equal(200, (await planner.PostWithHeadersAsync(path, Json(Activity(title)), Key(key))).Status);
        // As if the server stopped after the work was committed and before its answer was recorded.
        await app.ScalarAsync("UPDATE iam.idempotency_keys SET status_code = NULL, response = NULL, completed_at = NULL WHERE key = @k", ("k", key));

        var (status, error, _) = await planner.PostWithHeadersAsync(path, Json(Activity(title)), Key(key));

        Assert.Equal((409, "request_in_progress"), (status, error.Code()));
        Assert.Equal(1, await ActivitiesTitled(title));
    }

    /// <summary>AC-I5: keys are each user's own; a malformed key is refused before anything runs.</summary>
    [Fact]
    public async Task Keys_are_per_user_and_must_be_well_formed()
    {
        var key = Guid.NewGuid().ToString();
        var empty = new JsonObject();
        Assert.Equal(200, (await (await app.As("planner")).PostWithHeadersAsync("/api/notifications/read-all", Json(empty), Key(key))).Status);
        var other = await (await app.As("sa1")).PostWithHeadersAsync("/api/notifications/read-all", Json(empty), Key(key));
        Assert.Equal(200, other.Status);
        Assert.False(other.Headers.ContainsKey("Idempotent-Replayed"));
        Assert.Equal(2, await app.CountAsync("SELECT count(*) FROM iam.idempotency_keys WHERE key = @k", ("k", key)));

        var (status, error, _) = await (await app.As("planner")).PostWithHeadersAsync("/api/notifications/read-all", Json(empty), Key("not a key!"));
        Assert.Equal((400, "invalid_idempotency_key"), (status, error.Code()));
    }

    /// <summary>AC-I6: a retried upload of the same file with the same key gets the first answer, not a duplicate-file refusal.</summary>
    [Fact]
    public async Task A_retried_upload_gets_the_first_answer()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var item = form.ItemsOf().First();
        var entry = await app.As("entry");
        await entry.Ok("PUT", $"/api/forms/{form["id"].Str()}/values", Calls.InputFor(item));
        var path = $"/api/forms/{form["id"].Str()}/values/{item["indicatorId"].Str()}/evidence";
        var file = Env.UniquePdf();
        var key = Guid.NewGuid().ToString();

        MultipartFormDataContent Form()
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return new MultipartFormDataContent { { content, "file", "evidence.pdf" } };
        }

        var first = await entry.PostWithHeadersAsync(path, Form(), Key(key));
        var retry = await entry.PostWithHeadersAsync(path, Form(), Key(key));

        Assert.Equal((200, 200), (first.Status, retry.Status));
        Assert.Equal(first.Body!["id"].Str(), retry.Body!["id"].Str());
        Assert.Equal("true", retry.Headers["Idempotent-Replayed"]);
    }

    /// <summary>AC-I7: sign-in is never replayed from a stored answer, and spent keys are removed by the retention job.</summary>
    [Fact]
    public async Task Sign_in_keeps_no_key_and_spent_keys_are_removed()
    {
        var key = Guid.NewGuid().ToString();
        using (var http = new HttpClient { BaseAddress = app.Base })
        {
            var csrf = JsonNode.Parse(await http.GetStringAsync("/api/auth/csrf"))!["token"].Str()!;
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = Json(Scenario.O(("email", "planner@tier1.local"), ("password", Tier1App.Password))),
            };
            login.Headers.Add("X-XSRF-TOKEN", csrf);
            login.Headers.Add("Idempotency-Key", key);
            using var response = await http.SendAsync(login);
            Assert.Equal(200, (int)response.StatusCode);
        }
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM iam.idempotency_keys WHERE key = @k", ("k", key)));

        var (planner, path) = await ActivitiesAsync();
        var old = Guid.NewGuid().ToString();
        Assert.Equal(200, (await planner.PostWithHeadersAsync(path, Json(Activity($"نشاط قديم {Calls.Tag()}")), Key(old))).Status);
        await app.ScalarAsync("UPDATE iam.idempotency_keys SET created_at = now() - interval '3 days' WHERE key = @k", ("k", old));

        await app.Services.GetServices<IHostedService>().OfType<RetentionJob>().Single().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM iam.idempotency_keys WHERE key = @k", ("k", old)));
    }
}
