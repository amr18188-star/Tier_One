using Microsoft.Extensions.DependencyInjection;

namespace Tier1.IntegrationTests;

/// <summary>BE-6 and BE-7: refused input gets one error shape with a trace id, and enum inputs are names the code defines.</summary>
[Collection("app")]
public sealed class InputValidationTests(Tier1App app)
{
    [Fact]
    public async Task An_enum_as_a_number_or_an_unknown_name_is_refused()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var item = form.ItemsOf().First();
        var entry = await app.As("entry");
        var path = $"/api/forms/{form["id"].Str()}/values";

        foreach (var kind in new JsonNode?[] { JsonValue.Create(7), JsonValue.Create("7"), JsonValue.Create("Guess") })
        {
            var body = Scenario.O(("indicatorId", item["indicatorId"]), ("kind", kind), ("value", null), ("numerator", null), ("denominator", null),
                ("reason", "سبب يكفي للاختبار"), ("explanation", null), ("version", null));
            var (status, error) = await entry.Call("PUT", path, body);
            Assert.True(status == 400, $"kind {kind?.ToJsonString()} gave {status} {error?.ToJsonString()}");
            Assert.Equal("bad_request", error.Code());
        }
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM dc.values WHERE submission_id = @id", ("id", Guid.Parse(form["id"].Str()!))));

        var planner = await app.As("planner");
        var (bad, badError) = await planner.Call("GET", $"/api/forms?cycleId={cycle.Id}&status=7");
        Assert.Equal((400, "invalid_value"), (bad, badError.Code()));
        var (good, _) = await planner.Call("GET", $"/api/forms?cycleId={cycle.Id}&status=InProgress");
        Assert.Equal(200, good);
    }

    [Fact]
    public async Task Every_error_carries_a_code_and_the_trace_id_of_its_response()
    {
        // BE-6: outside Development binding errors were bodiless 400s; the option is now on in every environment.
        Assert.True(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Routing.RouteHandlerOptions>>().Value.ThrowOnBadRequest);

        using var anonymous = new HttpClient { BaseAddress = app.Base };
        foreach (var (path, status, code) in new[] { ("/api/no-such-route", 404, "not_found"), ("/api/forms", 401, "unauthenticated") })
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(status, (int)response.StatusCode);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.Equal(code, body["code"].Str());
            var header = Assert.Single(response.Headers.GetValues("X-Trace-Id"));
            Assert.Equal(header, body["traceId"].Str());
        }
    }
}
