using Microsoft.Extensions.DependencyInjection;
using Tier1.Application.Abstractions;
using Tier1.Application.Integration;
using Tier1.Domain.Integration;

namespace Tier1.IntegrationTests;

/// <summary>A test-only event; its two handlers are registered by the fixture.</summary>
public sealed record ProbeEvent(string Tag) : IDomainEvent
{
    public static string EventType => "it.probe";
}

/// <summary>Writes one mail per run, so a rerun would show as a second mail.</summary>
public sealed class ProbeMailHandler(IMailOutbox mail) : DomainEventHandler<ProbeEvent>
{
    public override string Name => "it.probe.mail";

    protected override Task HandleAsync(ProbeEvent evt, DomainEventRecord record, CancellationToken ct)
    {
        mail.Enqueue("it.probe", "probe@tier1.local", $"probe-{evt.Tag}", "x");
        return Task.CompletedTask;
    }
}

/// <summary>Fails while its tag is in <see cref="FailOnce"/>, then succeeds.</summary>
public sealed class ProbeFlakyHandler : DomainEventHandler<ProbeEvent>
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> FailOnce = new();

    public override string Name => "it.probe.flaky";

    protected override Task HandleAsync(ProbeEvent evt, DomainEventRecord record, CancellationToken ct) =>
        FailOnce.TryRemove(evt.Tag, out _) ? throw new InvalidOperationException("Probe failure") : Task.CompletedTask;
}

/// <summary>BE-3 / phase 3: business events commit with the change and are handled after it, each handler once.</summary>
[Collection("app")]
public sealed class EventOutboxTests(Tier1App app)
{
    [Fact]
    public async Task A_failed_handler_is_retried_without_rerunning_the_one_that_succeeded()
    {
        await app.As("planner");
        var tag = Calls.Tag();
        ProbeFlakyHandler.FailOnce[tag] = true;
        Guid id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IEventOutbox>().Add(new ProbeEvent(tag), "probe", Guid.NewGuid());
            var db = scope.ServiceProvider.GetRequiredService<IAppDb>();
            id = db.ChangeTracker.Entries<DomainEventRecord>().Single().Entity.Id;
            await db.SaveChangesAsync();
        }

        await WaitAsync(async () => await app.ScalarAsync("SELECT processed_at IS NOT NULL FROM \"int\".outbox_events WHERE id = @id", ("id", id)) is true);

        Assert.Equal(1, await app.CountAsync("SELECT count(*) FROM adm.mail_outbox WHERE subject = @s", ("s", $"probe-{tag}")));
        Assert.Equal(2, await app.CountAsync("SELECT count(*) FROM \"int\".processed_events WHERE event_id = @id", ("id", id)));
        Assert.Equal(2, Convert.ToInt32(await app.ScalarAsync("SELECT attempts FROM \"int\".outbox_events WHERE id = @id", ("id", id)), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_form_transition_raises_one_event_and_its_mail_follows_the_commit()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.SubmittedEngineeringFormAsync(app, cycle);
        var id = Guid.Parse(form["id"].Str()!);
        Assert.Equal(1, await app.CountAsync("SELECT count(*) FROM \"int\".outbox_events WHERE aggregate_id = @id AND type = 'form.moved'", ("id", id)));
        await WaitAsync(async () => await app.CountAsync("SELECT count(*) FROM adm.mail_outbox WHERE kind = 'dc.form' AND subject LIKE '%' || @name || '%'", ("name", cycle.Name)) > 0);
    }

    /// <summary>Waits for the background dispatcher (a failed handler retries after 5 seconds).</summary>
    internal static async Task WaitAsync(Func<Task<bool>> done, int seconds = 30)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!await done())
        {
            if (DateTime.UtcNow > until) Assert.Fail($"Not done within {seconds} s.");
            await Task.Delay(250);
        }
    }
}
