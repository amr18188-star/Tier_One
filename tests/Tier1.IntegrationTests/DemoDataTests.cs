using Microsoft.Extensions.Logging.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>
/// The demo data loader (Seed:DemoData) goes through the public API with every role, so each contract change reaches it.
/// It runs only on a new database, where no test of the suite would notice it breaking; this runs it whole on the test database.
/// </summary>
[Collection("app")]
public sealed class DemoDataTests(Tier1App app)
{
    [Fact]
    public async Task The_demo_data_loads_through_the_api()
    {
        using var run = new DemoRun(app.Base, Tier1App.Password, NullLogger.Instance);
        await run.ExecuteAsync(CancellationToken.None);

        var planner = await app.As("planner");
        var cycles = (await planner.Get("/api/cycles")).Items().Select(c => c["name"].Str()).ToList();
        Assert.Contains(cycles, n => n!.Contains("الربع الأول 2027", StringComparison.Ordinal));
        Assert.Contains(cycles, n => n!.Contains("الربع الثالث 2027", StringComparison.Ordinal));
    }
}
