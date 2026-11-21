using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Application.Scorecard;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;

namespace Tier1.Application.Settings;

/// <param name="Editable">The current user may change it (the super administrator).</param>
public sealed record PlanPeriodDto(
    int StartYear, int StartMonth, int EndYear, int EndMonth, DateOnly Start, DateOnly End, IReadOnlyList<int> Years, int Quarters,
    bool Editable, DateTimeOffset? UpdatedAt);

public sealed record SavePlanPeriodRequest(int StartYear, int StartMonth, int EndYear, int EndMonth, string? Reason);

/// <summary>
/// ADR-0017: the plan's period, set by the super administrator as the Ministry of Higher Education instructs (default 1 September
/// 2026 to 31 August 2031), from which the quarters, the plan years and the target years follow everywhere. The four settings
/// change together, checked: whole plan years, and no official quarterly cycle left outside the period. Every change is audited.
/// </summary>
public sealed class PlanPeriodService(ISettingsStore db, IReadModel read, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, IMemoryCache cache)
{
    private const string CacheKey = "plan-period";

    /// <summary>The four keys as an array, so EF Core sends them to SQL as one array parameter (a set does not translate).</summary>
    private static readonly string[] Keys = [.. SettingKeys.PlanPeriodKeys];

    /// <summary>The plan period in force (kept 30 seconds, and dropped when it is saved here).</summary>
    public async Task<PlanPeriod> CurrentAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var saved = await db.Settings.AsNoTracking().Where(s => Keys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
            int Value(string key, int fallback) => saved.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
            var d = PlanPeriod.Default;
            var period = new PlanPeriod(Value(SettingKeys.PlanStartYear, d.StartYear), Value(SettingKeys.PlanStartMonth, d.StartMonth),
                Value(SettingKeys.PlanEndYear, d.EndYear), Value(SettingKeys.PlanEndMonth, d.EndMonth));
            // A start saved before the end existed (the old start-only setting) keeps the plan's five years.
            if (!saved.ContainsKey(SettingKeys.PlanEndYear) && !saved.ContainsKey(SettingKeys.PlanEndMonth))
            {
                var end = period.Start.AddMonths(12 * d.YearCount - 1);
                period = period with { EndYear = end.Year, EndMonth = end.Month };
            }
            return PlanPeriod.Problem(period) is null ? period : d;
        }) ?? PlanPeriod.Default;

    public async Task<PlanPeriodDto> GetAsync(CancellationToken ct)
    {
        var me = await access.RequireAsync(SettingsService.Feature, Cap.View, ct);
        return await DtoAsync(await CurrentAsync(ct), me.IsSuperAdmin, ct);
    }

    public async Task<PlanPeriodDto> SaveAsync(SavePlanPeriodRequest req, CancellationToken ct)
    {
        var me = await access.CurrentAsync(ct);
        if (!me.IsSuperAdmin)
            throw DomainException.SeparationOfDuties("SOD-10", "فترة الخطة يحددها المدير الأعلى وحده، بتعليمات وزارة التعليم العالي.",
                "Only a super administrator sets the plan period.");
        var reason = Require.Reason(req.Reason);
        var period = new PlanPeriod(req.StartYear, req.StartMonth, req.EndYear, req.EndMonth);
        if (req.StartYear is < 2024 or > 2040)
            throw new DomainException("invalid_plan_period", "سنة البداية من 2024 إلى 2040.", "The start year is 2024 to 2040.", 400) { Field = "startYear" };
        if (PlanPeriod.Problem(period) is { } problem)
            throw new DomainException("invalid_plan_period", problem, "The plan period is whole plan years of twelve months, one to ten.", 400) { Field = "endMonth" };

        // DEC (ADR-0017): an official quarterly cycle never falls outside the plan; move or close the period around it instead.
        var outside = (await read.Query<CollectionCycle>().AsNoTracking().Where(c => !c.Trial && c.Quarter != null)
                .Select(c => new { c.Name, c.TargetYear, Quarter = c.Quarter!.Value }).ToListAsync(ct))
            .Where(c => !period.HasQuarter(c.TargetYear, c.Quarter)).Select(c => c.Name).ToList();
        if (outside.Count > 0)
            throw new DomainException("plan_period_orphans_cycles",
                $"دورات رسمية تقع خارج الفترة الجديدة: {string.Join("، ", outside.Take(5))}{(outside.Count > 5 ? "…" : "")}. لم يتغير شيء.",
                $"{outside.Count} official cycle(s) would fall outside the new period. Nothing was changed.");

        var before = await CurrentAsync(ct);
        var now = clock.UtcNow;
        var values = new Dictionary<string, int>
        {
            [SettingKeys.PlanStartYear] = period.StartYear, [SettingKeys.PlanStartMonth] = period.StartMonth,
            [SettingKeys.PlanEndYear] = period.EndYear, [SettingKeys.PlanEndMonth] = period.EndMonth,
        };
        var rows = await db.Settings.Where(s => Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        foreach (var (key, value) in values)
        {
            if (!rows.TryGetValue(key, out var row)) db.Settings.Add(row = new SystemSetting { Key = key });
            row.Value = value.ToString(CultureInfo.InvariantCulture);
            row.UpdatedAt = now;
            row.UpdatedBy = current.UserId;
        }
        audit.Record(AuditEvents.Audit("adm", SettingsService.Feature, "adm.plan_period_update", "تعديل فترة الخطة", "Changed the plan period") with
        {
            Permission = $"{SettingsService.Feature}:E",
            Target = new AuditTarget("Setting", "plan_period", "فترة الخطة"),
            Changes =
            [
                new FieldChange("start", Month(before.Start), Month(period.Start)),
                new FieldChange("end", Month(before.End), Month(period.End)),
            ],
            Reason = reason,
            Details = new { years = period.Years, quarters = period.QuarterCount },
        });
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
        foreach (var key in SettingKeys.PlanPeriodKeys) cache.Remove("setting:" + key);
        return await DtoAsync(period, true, ct);
    }

    private static string Month(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<PlanPeriodDto> DtoAsync(PlanPeriod p, bool editable, CancellationToken ct)
    {
        var updated = await db.Settings.AsNoTracking().Where(s => Keys.Contains(s.Key)).MaxAsync(s => (DateTimeOffset?)s.UpdatedAt, ct);
        return new PlanPeriodDto(p.StartYear, p.StartMonth, p.EndYear, p.EndMonth, p.Start, p.End, p.Years, p.QuarterCount, editable, updated);
    }
}
