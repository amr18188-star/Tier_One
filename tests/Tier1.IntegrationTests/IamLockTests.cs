using Microsoft.Extensions.DependencyInjection;
using Tier1.Application.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>ARCH-2: the IAM lock behind its port still serialises reserved-role changes across connections (SOD-10).</summary>
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
}
