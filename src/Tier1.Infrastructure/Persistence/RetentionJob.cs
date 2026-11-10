using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// الاحتفاظ بالبيانات: يمحو مرة كل ست ساعات ما انتهت حاجته ولا قيمة رقابية له: الجلسات المنتهية أو الملغاة، وروابط البريد المستعملة أو المنتهية،
/// ومحاولات صفحة التحقق العامة (فيها عناوين الزوار) بعد مدتها. ما يلزم الرقابة باق في سجلات MongoDB.
/// </summary>
public sealed class RetentionJob(IServiceScopeFactory scopes, IConfiguration config, ILogger<RetentionJob> log) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Every, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Retention cleanup failed; it retries later.");
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var now = DateTimeOffset.UtcNow;
        var sessions = now.AddDays(-config.GetValue("Retention:SessionDays", 30));
        var staff = await db.Sessions.Where(s => s.ExpiresAt < sessions || s.RevokedAt < sessions).ExecuteDeleteAsync(ct);
        if (staff > 0) log.LogInformation("Retention removed {Staff} expired staff sessions.", staff);
    }
}
