using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tier1.Application.Collection;
using Tier1.Domain.Collection;
using Tier1.Infrastructure.Persistence;

namespace Tier1.IntegrationTests;

/// <summary>
/// PERF-B1: notification recipients are found among the accounts that can hold the access, not by computing every account's
/// permissions. The result must be the same as the exhaustive way, for every unit and every review step.
/// </summary>
[Collection("app")]
public sealed class RecipientsTests(Tier1App app)
{
    [Fact]
    public async Task Narrowed_recipients_equal_the_exhaustive_ones()
    {
        await app.As("planner"); // the app and its seeded accounts are up
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<FormNotifier>();
        var units = await db.OrgUnits.AsNoTracking().Where(u => u.Active).ToListAsync();
        Assert.NotEmpty(units);
        var compared = 0;
        foreach (var unit in units)
        {
            foreach (var step in new[] { ApprovalStep.DepartmentReview, ApprovalStep.CollegeCommittee, ApprovalStep.Dean, ApprovalStep.Quality })
            {
                var narrow = await notifier.ReviewersAsync(step, unit, CancellationToken.None);
                var all = await notifier.ReviewersAsync(step, unit, CancellationToken.None, everyone: true);
                Assert.True(all.SequenceEqual(narrow), $"{unit.Code} {step}: [{string.Join(",", narrow)}] vs [{string.Join(",", all)}]");
                compared += all.Count;
            }
            Assert.Equal(await notifier.UnitEditorsAsync(unit, CancellationToken.None, everyone: true), await notifier.UnitEditorsAsync(unit, CancellationToken.None));
        }
        Assert.True(compared > 0, "Some reviewers were found at all");
    }
}
