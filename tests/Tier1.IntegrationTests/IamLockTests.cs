using Microsoft.Extensions.DependencyInjection;
using Tier1.Application.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>ARCH-2 and BE-14: the lock ports still serialise check-then-write work across connections (SOD-10, BE-5).</summary>
[Collection("app")]
public sealed class IamLockTests(Tier1App app)
{
    [Fact]
    public async Task A_second_holder_waits_until_the_first_commits()
    {
        await using var first = app.Services.CreateAsyncScope();
        await using var second = app.Services.CreateAsyncScope();
        var held = await first.ServiceProvider.GetRequiredService<IIamLock>().BeginAsync(default);

        var waiting = second.ServiceProvider.GetRequiredService<IIamLock>().BeginAsync(default);
        await Task.Delay(500);
        Assert.False(waiting.IsCompleted, "The second transaction must wait for the lock");

        await held.CommitAsync();
        var acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await acquired.RollbackAsync();
    }

    [Fact]
    public async Task A_named_transaction_lock_serialises_the_same_key_only()
    {
        await using var first = app.Services.CreateAsyncScope();
        await using var second = app.Services.CreateAsyncScope();
        await using var other = app.Services.CreateAsyncScope();
        var held = await first.ServiceProvider.GetRequiredService<ITransactionLock>().BeginAsync(7_000_001, default);

        // Another key is not blocked.
        var free = await other.ServiceProvider.GetRequiredService<ITransactionLock>().BeginAsync(7_000_002, default).WaitAsync(TimeSpan.FromSeconds(10));
        await free.RollbackAsync();

        var waiting = second.ServiceProvider.GetRequiredService<ITransactionLock>().BeginAsync(7_000_001, default);
        await Task.Delay(500);
        Assert.False(waiting.IsCompleted, "The same key waits for the holder");
        await held.CommitAsync();
        var acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await acquired.RollbackAsync();
    }
}
