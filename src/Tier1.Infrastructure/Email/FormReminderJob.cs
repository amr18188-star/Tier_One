using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;
using Tier1.Application.Collection;
using Tier1.Application.Settings;
using Tier1.Domain.Common;

namespace Tier1.Infrastructure.Email;

/// <summary>
/// تذكير يومي بنماذج الدورات المفتوحة التي اقترب موعدها أو تجاوزته، لمن عليه الإجراء الآن (المدخل أو المراجع).
/// يفحص كل ساعة ويرسل بعد الثامنة صباحًا بتوقيت القاهرة، ومفتاح منع التكرار يجعل التذكير مرة واحدة في اليوم لكل نموذج ولو تعددت النسخ.
/// </summary>
public sealed class FormReminderJob(IServiceScopeFactory scopes, IClock clock, Persistence.JobLock jobs, ILogger<FormReminderJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
                await jobs.RunExclusiveAsync(Persistence.JobLock.Reminders, RunOnceAsync, stoppingToken);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Form reminders failed; they retry within the hour.");
            }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var local = CairoTime.ToCairo(clock.UtcNow);
        if (local.Hour < 8) return 0;
        await using var scope = scopes.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var days = await settings.IntAsync(SettingKeys.ReminderDaysBeforeDue, ct);
        var sent = await scope.ServiceProvider.GetRequiredService<FormNotifier>().RemindAsync(DateOnly.FromDateTime(local.DateTime), days, ct);
        if (sent > 0) log.LogInformation("Queued {Count} form reminders.", sent);
        return sent;
    }
}
