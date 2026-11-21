namespace Tier1.IntegrationTests;

/// <summary>
/// ADR-0017: the plan's period (default 1 September 2026 – 31 August 2031, quarters of three months) is set by the super
/// administrator alone; every quarter, plan year and target year follows it, and no official cycle is left outside it.
/// </summary>
[Collection("app")]
public sealed class PlanPeriodTests(Tier1App app)
{
    private const string Path = "/api/admin/settings/plan-period";

    private static JsonObject Period(int sy, int sm, int ey, int em, string? reason = "تعليمات وزارة التعليم العالي") =>
        Scenario.O(("startYear", sy), ("startMonth", sm), ("endYear", ey), ("endMonth", em), ("reason", reason));

    /// <summary>AC-P1: the default period is in force, read by everyone who sees the settings, and kept out of the plain settings list.</summary>
    [Fact]
    public async Task The_default_period_runs_from_september_2026_to_august_2031()
    {
        var sa = await app.As("sa1");
        var p = (await sa.Get(Path))!;
        Assert.Equal(("2026-09-01", "2031-08-31", 20m, true), (p["start"].Str(), p["end"].Str(), p["quarters"].Num(), p["editable"].Truthy()));
        Assert.Equal([2027m, 2028m, 2029m, 2030m, 2031m], p["years"].Items().Select(y => y.Num()));

        var calendar = (await sa.Get("/api/plan/calendar")).Items().ToList();
        Assert.Equal((20, "2026-09-01", "2031-06-01"), (calendar.Count, calendar[0]["start"].Str(), calendar[^1]["start"].Str()));

        Assert.DoesNotContain((await sa.Get("/api/admin/settings")).Items(), s => s["key"].Str()!.StartsWith("plan_", StringComparison.Ordinal));
        var (status, error) = await sa.Call("PUT", "/api/admin/settings/plan_end_year", Scenario.O(("value", 2032), ("reason", "محاولة من خارج فترة الخطة")));
        Assert.Equal((422, "plan_period_only"), (status, error.Code()));
    }

    /// <summary>AC-P4: only the super administrator changes the period (SOD-10); a reason is required; the period is whole plan years.</summary>
    [Fact]
    public async Task Only_the_super_administrator_sets_a_valid_period()
    {
        var sysadmin = await app.As("sysadmin");
        Assert.Equal(403, (await sysadmin.Call("PUT", Path, Period(2026, 9, 2032, 8))).Status);
        Assert.Equal(403, (await (await app.As("planner")).Call("PUT", Path, Period(2026, 9, 2032, 8))).Status);

        var sa = await app.As("sa1");
        Assert.Equal(400, (await sa.Call("PUT", Path, Period(2026, 9, 2032, 8, reason: ""))).Status);
        var (status, error) = await sa.Call("PUT", Path, Period(2026, 9, 2031, 6));
        Assert.Equal((400, "invalid_plan_period", "endMonth"), (status, error.Code(), error!["errors"]!.AsArray().First()!["field"].Str()));
        Assert.Equal("2031-08-31", (await sa.Get(Path))!["end"].Str());
    }

    /// <summary>AC-P5: a period that would leave an official quarterly cycle outside it is refused, and nothing changes.</summary>
    [Fact]
    public async Task A_period_that_orphans_an_official_cycle_is_refused()
    {
        // An official cycle of its own, in a quarter no other test or demo run uses (the demo data takes 2027, CycleTests 2031 Q4).
        // It is removed afterwards, so the shared database keeps no official cycle from this test.
        var planner = await app.As("planner");
        var tag = Calls.Tag();
        var cycle = await planner.Ok("POST", "/api/cycles", Scenario.O(("name", $"دورة رسمية {tag}"), ("periodLabel", tag), ("targetYear", 2030),
            ("opensOn", Calls.Today()), ("dueOn", Calls.Today(14)), ("trial", false), ("quarter", 2), ("version", null)));
        try
        {
            var sa = await app.As("sa1");
            var (status, error) = await sa.Call("PUT", Path, Period(2026, 9, 2029, 8));
            Assert.Equal((422, "plan_period_orphans_cycles"), (status, error.Code()));
            Assert.Contains(tag, error!["title"].Str());
            Assert.Equal("2031-08-31", (await sa.Get(Path))!["end"].Str());
        }
        finally
        {
            await app.ScalarAsync("DELETE FROM dc.cycles WHERE id = @id", ("id", Guid.Parse(cycle["id"].Str()!)));
        }
    }

    /// <summary>AC-P2, AC-P6: a valid change moves the calendar and the target years at once, and is audited with its reason.</summary>
    [Fact]
    public async Task A_new_period_moves_the_calendar_and_the_target_years()
    {
        var sa = await app.As("sa1");
        var tag = Calls.Tag();
        var reason = $"تمديد الخطة سنة {tag}";
        try
        {
            var saved = await sa.Ok("PUT", Path, Period(2026, 9, 2032, 8, reason));
            Assert.Equal(("2032-08-31", 24m), (saved["end"].Str(), saved["quarters"].Num()));

            var calendar = (await sa.Get("/api/plan/calendar")).Items().ToList();
            Assert.Equal((24, 2032m), (calendar.Count, calendar[^1]["year"].Num()));
            var code = (await sa.Get("/api/kpi/indicators")).Items().First()["code"].Str();
            Assert.Equal(2032m, (await sa.Get($"/api/kpi/indicators/{code}/targets"))!["years"].Items().Last().Num());
            // The payload keeps Arabic escaped, so the entry is found by the reason's Latin tag.
            Assert.True(await app.CountAsync("SELECT count(*) FROM logs.outbox WHERE payload::text LIKE '%adm.plan_period_update%' AND payload::text LIKE '%' || @t || '%'", ("t", tag)) >= 1);
        }
        finally
        {
            await sa.Ok("PUT", Path, Period(2026, 9, 2031, 8, "إعادة الفترة الافتراضية بعد الاختبار"));
        }
        Assert.Equal(20, (await sa.Get("/api/plan/calendar")).Items().Count());
    }
}
