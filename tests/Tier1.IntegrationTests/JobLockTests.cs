using Microsoft.Extensions.DependencyInjection;
using Tier1.Infrastructure.Persistence;

namespace Tier1.IntegrationTests;

/// <summary>BE-15: a scheduled job runs on one instance at a time, and a reminder reaches each person once a day whatever the order.</summary>
[Collection("app")]
public sealed class JobLockTests(Tier1App app)
{
    [Fact]
    public async Task A_second_instance_skips_a_job_that_is_running_and_runs_it_after()
    {
        var jobs = app.Services.GetRequiredService<JobLock>();
        const long key = 7_499_999;
        var started = new TaskCompletionSource();
        var finish = new TaskCompletionSource();
        var first = jobs.RunExclusiveAsync(key, async _ =>
        {
            started.SetResult();
            await finish.Task;
        }, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var ranWhileHeld = false;
        Assert.False(await jobs.RunExclusiveAsync(key, _ => { ranWhileHeld = true; return Task.CompletedTask; }, default));
        Assert.False(ranWhileHeld);

        finish.SetResult();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(10)));
        var ranAfter = false;
        Assert.True(await jobs.RunExclusiveAsync(key, _ => { ranAfter = true; return Task.CompletedTask; }, default));
        Assert.True(ranAfter);
    }

    [Fact]
    public async Task A_failing_run_releases_the_job()
    {
        var jobs = app.Services.GetRequiredService<JobLock>();
        const long key = 7_499_998;
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.RunExclusiveAsync(key, _ => throw new InvalidOperationException("boom"), default));
        Assert.True(await jobs.RunExclusiveAsync(key, _ => Task.CompletedTask, default));
    }

    [Fact]
    public async Task Reminders_run_twice_in_a_day_reach_each_person_once()
    {
        await Flow.OpenCycleAsync(app);
        var today = Tier1.Domain.Common.CairoTime.DateOf(DateTimeOffset.UtcNow);
        async Task<int> RemindAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<Tier1.Application.Collection.FormNotifier>().RemindAsync(today, 30, default);
        }
        const string Reminders = "SELECT count(*) FROM adm.mail_outbox WHERE kind = 'dc.reminder' AND dedup_key LIKE @day";
        var day = $"rem:%:{today:yyyyMMdd}:%";

        Assert.True(await RemindAsync() > 0, "The open cycle's forms are due within 30 days");
        var after = await app.CountAsync(Reminders, ("day", day));
        Assert.Equal(0, await RemindAsync());
        Assert.Equal(after, await app.CountAsync(Reminders, ("day", day)));
        // BE-15: the key names the recipient, not a counter that depends on the order rows came back in.
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM adm.mail_outbox WHERE kind = 'dc.reminder' AND dedup_key LIKE @day AND dedup_key !~ ':[0-9a-f]{32}$'", ("day", day)));
    }
}
