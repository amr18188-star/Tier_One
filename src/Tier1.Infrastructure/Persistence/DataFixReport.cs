using Microsoft.Extensions.DependencyInjection;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// The pending data fixes and what each changes (ADR-0010). Printed at the end of the API's <c>--migrate</c> and by the tools'
/// <c>--data-fixes</c>; listing changes nothing, and fixes are applied only by the tools' <c>--apply-data-fixes</c> with an approver.
/// </summary>
public static class DataFixReport
{
    /// <summary>Writes the pending fixes; the exit code is always 0 (listing changes nothing).</summary>
    public static async Task<int> WriteAsync(IServiceProvider services, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        await using var scope = services.CreateAsyncScope();
        var pending = await scope.ServiceProvider.GetRequiredService<Seeder>().PendingDataFixesAsync(CancellationToken.None).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            await output.WriteLineAsync("No data fixes are pending.").ConfigureAwait(false);
            return 0;
        }
        await output.WriteLineAsync(
            $"{pending.Count} data fix(es) await approval (apply with Tier1.Tools --apply-data-fixes --confirm --operator \"<name>\"):").ConfigureAwait(false);
        foreach (var f in pending) await output.WriteLineAsync($"  - {f.Key}: {f.DescriptionEn} | {f.DescriptionAr}").ConfigureAwait(false);
        return 0;
    }
}
