using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tier1.Application.Collection;
using Tier1.Domain.Collection;
using Tier1.Infrastructure.Persistence;
using Tier1.TestSupport;

namespace Tier1.Tools;

/// <summary>
/// <c>--growth-data</c>: brings a development database to the size the system is expected to reach, for load tests: about 250
/// units (the official structure), 300 indicators and 20 collection cycles with their forms and values. Development only.
/// <list type="bullet">
/// <item>Indicators and cycles go through the API as the planner, so the dictionary and cycle rules and the audit apply; opening
/// a cycle creates its forms the real way. The cycles are trial cycles ("growth-NN"), so they never take an official quarter.</item>
/// <item>The values and approval states of their forms are written directly through the server's data context: the official
/// structure has no data-entry accounts, and typing hundreds of thousands of values through the API would take hours. Each value
/// is checked by the same <see cref="ValueRules"/> as a typed one. These writes are not in the audit log.</item>
/// </list>
/// Safe to run again: it creates only what is missing and fills only cycles without values.
/// </summary>
internal static class GrowthData
{
    private const string Prefix = "growth-";

    public static async Task<int> RunAsync(string[] args, IConfiguration config, IHostEnvironment environment, IServiceProvider services)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("--growth-data writes generated data into the database; it runs in Development only (DOTNET_ENVIRONMENT=Development).");
        var server = new Uri(Maintenance.Option(args, "--base") ?? "http://localhost:5150");
        var password = Maintenance.Option(args, "--password") ?? config["Seed:DemoPassword"]
            ?? throw new ArgumentException("No demo password: pass --password or set Seed:DemoPassword.");
        var targetIndicators = int.Parse(Maintenance.Option(args, "--indicators") ?? "300", CultureInfo.InvariantCulture);
        var targetCycles = int.Parse(Maintenance.Option(args, "--cycles") ?? "20", CultureInfo.InvariantCulture);
        var wait = TimeSpan.FromSeconds(int.Parse(Maintenance.Option(args, "--wait") ?? "300", CultureInfo.InvariantCulture));

        using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var log = loggers.CreateLogger("GrowthData");
        var clock = Stopwatch.StartNew();
        await DemoData.WaitForServerAsync(server, wait, log);
        using var planner = await DemoClient.LoginAsync(server, "planner@tier1.local", password, CancellationToken.None);

        await AddIndicatorsAsync(planner, targetIndicators, log);
        var cycles = await AddCyclesAsync(planner, targetCycles, log);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var plannerId = await db.Users.Where(u => u.Email == "planner@tier1.local").Select(u => u.Id).FirstAsync();
            for (var k = 0; k < cycles.Count; k++)
                await FillAsync(db, cycles[k], plannerId, latest: k == cycles.Count - 1, log);
        }
        // Every growth cycle but the latest is closed, as past quarters are.
        foreach (var cycle in cycles.Take(cycles.Count - 1))
            await planner.CallAsync(HttpMethod.Post, $"/api/cycles/{cycle}/close", new JsonObject { ["note"] = "إغلاق دورة بيانات النمو" }, null, CancellationToken.None);

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
            log.LogInformation("Growth data ready in {Seconds:0} s: {Units} units, {Indicators} indicators, {Cycles} cycles, {Forms} forms, {Values} values.",
                clock.Elapsed.TotalSeconds, await db.OrgUnits.CountAsync(u => u.Active), await db.Indicators.CountAsync(), await db.Cycles.CountAsync(),
                await db.Submissions.CountAsync(), await db.SubmittedValues.CountAsync());
        }
        return 0;
    }

    /// <summary>New indicators up to the target, spread over the standards: two of three collected per college, one per department.</summary>
    private static async Task AddIndicatorsAsync(DemoClient planner, int target, ILogger log)
    {
        var existing = (await planner.GetAsync("/api/kpi/indicators", CancellationToken.None)).Items().Count();
        var standards = (await planner.GetAsync("/api/kpi/standards", CancellationToken.None)).Items().Select(s => s["code"].Str()!).ToList();
        if (existing >= target || standards.Count == 0)
        {
            log.LogInformation("Indicators: {Count} (target {Target}), none added.", existing, target);
            return;
        }
        for (var n = existing; n < target; n++)
        {
            var body = new JsonObject
            {
                ["standardCode"] = standards[n % standards.Count],
                ["nameAr"] = $"مؤشر بيانات النمو {n + 1:000}",
                ["unit"] = "عدد",
                ["level"] = n % 3 == 2 ? "Department" : "College",
                ["reason"] = "بيانات اختبار الأداء بحجم النمو المتوقع",
            };
            await planner.CallAsync(HttpMethod.Post, "/api/kpi/indicators", body, 200, CancellationToken.None);
        }
        log.LogInformation("Indicators: added {Added}, now {Target}.", target - existing, target);
    }

    /// <summary>The growth cycles (trial, "growth-NN"), created and opened up to the target; returns their ids, oldest first.</summary>
    private static async Task<List<Guid>> AddCyclesAsync(DemoClient planner, int target, ILogger log)
    {
        var mine = (await planner.GetAsync("/api/cycles", CancellationToken.None)).Items()
            .Where(c => c["periodLabel"].Str()?.StartsWith(Prefix, StringComparison.Ordinal) == true)
            .ToDictionary(c => c["periodLabel"].Str()!, c => c);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ids = new List<Guid>();
        for (var k = 1; k <= target; k++)
        {
            var label = $"{Prefix}{k:00}";
            if (!mine.TryGetValue(label, out var cycle))
            {
                var (_, created) = await planner.CallAsync(HttpMethod.Post, "/api/cycles", new JsonObject
                {
                    ["name"] = $"دورة بيانات النمو {k:00}", ["periodLabel"] = label, ["targetYear"] = Math.Min(2027 + (k - 1) / 4, 2031),
                    ["opensOn"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["dueOn"] = today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["trial"] = true, ["version"] = null,
                }, 200, CancellationToken.None);
                cycle = created!;
                log.LogInformation("Created cycle {Label}.", label);
            }
            var id = Guid.Parse(cycle["id"].Str()!);
            if (cycle["status"].Str() == "Draft")
                await planner.CallAsync(HttpMethod.Post, $"/api/cycles/{id}/open", null, 200, CancellationToken.None);
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Values for every indicator each form enters, checked by <see cref="ValueRules"/>; the forms approved (the latest cycle:
    /// three in five approved, the rest under review or in progress). A cycle that already has values is left as it is.
    /// </summary>
    private static async Task FillAsync(Tier1DbContext db, Guid cycleId, Guid editor, bool latest, ILogger log)
    {
        var forms = await db.Submissions.Where(s => s.CycleId == cycleId).ToListAsync();
        var ids = forms.Select(f => f.Id).ToList();
        if (await db.SubmittedValues.AnyAsync(v => ids.Contains(v.SubmissionId)))
        {
            log.LogInformation("Cycle {Cycle}: values already there, left as they are.", cycleId);
            return;
        }
        var cis = await db.CycleIndicators.AsNoTracking().Where(c => c.CycleId == cycleId).ToListAsync();
        var versionIds = cis.Select(c => c.VersionId).ToList();
        var specs = await db.IndicatorVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => Tier1.Application.Collection.CycleSpec.Of(v));
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id);
        var now = DateTimeOffset.UtcNow;
        var values = new List<SubmittedValue>();
        foreach (var form in forms)
        {
            var unit = units[form.UnitId];
            var rnd = new Rnd("growth", cycleId, form.UnitId);
            var share = latest ? rnd.Next() : 0;
            var (status, step) = share switch
            {
                < 0.6 => (SubmissionStatus.Approved, ApprovalStep.Done),
                < 0.85 => (SubmissionStatus.InReview, ApprovalStep.Quality),
                _ => (SubmissionStatus.InProgress, ApprovalStep.Entry),
            };
            foreach (var ci in cis.Where(c => FormAssignmentRules.Applies(c, unit)))
            {
                var spec = specs[ci.VersionId];
                var input = Input(spec, rnd);
                var (value, issues) = ValueRules.Check(spec, input, null);
                var ok = issues.All(i => i.Severity != IssueSeverity.Error);
                values.Add(new SubmittedValue
                {
                    SubmissionId = form.Id, IndicatorId = ci.IndicatorId, EditedBy = editor, EditedAt = now,
                    Kind = ok ? ValueKind.Number : ValueKind.NotAvailable, Value = ok ? value : null,
                    Numerator = ok ? input.Numerator : null, Denominator = ok ? input.Denominator : null,
                    Reason = ok ? null : "قيمة غير متاحة في بيانات النمو",
                });
            }
            db.Submissions.Attach(form);
            form.Status = status;
            form.Step = step;
            form.LastEditedBy = editor;
            form.LastEditedAt = now;
            form.SubmittedAt = status == SubmissionStatus.InProgress ? null : now;
            form.ApprovedAt = status == SubmissionStatus.Approved ? now : null;
            db.Entry(form).State = EntityState.Modified;
        }
        db.SubmittedValues.AddRange(values);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        log.LogInformation("Cycle {Cycle}: {Forms} forms, {Values} values.", cycleId, forms.Count, values.Count);
    }

    /// <summary>A plausible value for the indicator's unit and aggregation (a ratio as numerator and denominator, a weight when needed).</summary>
    private static ValueInput Input(ValueSpec spec, Rnd rnd)
    {
        if (ValueRules.UsesRatio(spec))
        {
            var d = rnd.Int(20, 400);
            return new ValueInput(ValueKind.Number, null, ValueRules.IsPercent(spec) ? rnd.Int(0, d) : rnd.Int(0, d * 3), d, null, null);
        }
        decimal v = ValueRules.IsPercent(spec) ? Math.Round((decimal)rnd.Uniform(30, 98), 1) : rnd.Int(0, 120);
        decimal? weight = spec.Aggregation == Domain.Kpi.Aggregation.WeightedAverage ? rnd.Int(10, 300) : null;
        return new ValueInput(ValueKind.Number, v, null, weight, null, null);
    }
}
