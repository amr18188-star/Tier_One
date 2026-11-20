namespace Tier1.IntegrationTests;

/// <summary>Phase 3 monitoring: readiness names what is wrong, and only a lost PostgreSQL makes it unavailable.</summary>
[Collection("app")]
public sealed class HealthTests(Tier1App app)
{
    [Fact]
    public async Task Readiness_reports_each_check_and_degrades_on_a_failed_event()
    {
        var planner = await app.As("planner");
        var ready = (await planner.Get("/health/ready"))!;
        var checks = ready["checks"]!.AsArray().ToDictionary(c => c!["name"].Str()!, c => c!["state"].Str());
        Assert.Equal(["audit", "events", "mail", "mongo", "postgres"], checks.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ok", checks["postgres"]);
        Assert.Equal("ok", checks["mongo"]);

        await app.ScalarAsync("INSERT INTO \"int\".outbox_events (id, type, aggregate_type, aggregate_id, payload, occurred_at, available_at, attempts, failed_at, last_error) " +
            "VALUES (gen_random_uuid(), 'it.health', 'probe', gen_random_uuid(), '{}', now(), now(), 10, now(), 'probe')");
        var degraded = (await planner.Get("/health/ready"))!;
        Assert.Equal("degraded", degraded["status"].Str());
        var events = degraded["checks"]!.AsArray().Single(c => c!["name"].Str() == "events")!;
        Assert.Equal(("degraded", "failed"), (events["state"].Str(), events["reason"].Str()));
        Assert.DoesNotContain("probe", degraded.ToJsonString(), StringComparison.Ordinal);
    }
}
