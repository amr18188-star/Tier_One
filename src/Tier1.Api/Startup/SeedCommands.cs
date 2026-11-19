using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Domain.Organization;
using Tier1.Infrastructure.Persistence;

namespace Tier1.Api.Startup;

/// <summary>
/// أوامر البيانات في الإنتاج (D10، ADR-0010): لا بذر عام تلقائي، والبيانات الأساسية وحدها بإجراء معتمد ومسجل وقابل للتكرار بأمان،
/// ولا يستبدل شيء من بيانات الإنتاج ولا يحذف.
/// <list type="bullet">
/// <item><c>--migrate</c>: ترحيل المخطط والأدوار الناقصة والبيانات المرجعية على قاعدة فارغة فقط، ثم يعرض الإصلاحات المعلقة دون تطبيقها.</item>
/// <item><c>--data-fixes</c>: يعرض الإصلاحات المعلقة وما يغيره كل منها.</item>
/// <item><c>--apply-data-fixes --confirm --operator "الاسم"</c>: يطبقها بموافقة، وكل إصلاح بقيد في سجل التدقيق.</item>
/// <item><c>--reset-kpi</c>: مرفوض في الإنتاج. <c>--load-structure</c>: في الإنتاج على قاعدة بلا جهات فقط (التهيئة الأولى).</item>
/// </list>
/// </summary>
public static class SeedCommands
{
    /// <summary>سبب رفض أمر في هذه البيئة، أو null إن جاز.</summary>
    public static string? Refusal(string command, bool production, int existingUnits) => command switch
    {
        "--reset-kpi" when production =>
            "--reset-kpi deletes every official value, form, value, document and cycle; it is refused in Production (ADR-0010). Use a staging copy.",
        "--load-structure" when production && existingUnits > 0 =>
            $"--load-structure in Production only loads the official structure on a database without units (first setup); this one has {existingUnits}. Nothing was changed (ADR-0010).",
        _ => null,
    };

    /// <summary>من وافق على التشغيل: إلزامي في الإنتاج، ويسجل في سجل التدقيق.</summary>
    public static string Operator(string[] args, bool production, string command)
    {
        var name = Maintenance.Option(args, "--operator")?.Trim();
        if (!string.IsNullOrWhiteSpace(name)) return $"{name} ({command})";
        if (production) throw new ArgumentException($"{command} in Production needs --operator \"<name of the approving administrator>\" (ADR-0010).");
        return $"{Environment.UserName} ({command})";
    }

    public static async Task<int> ExistingUnitsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        return await db.OrgUnits.CountAsync(u => u.Kind != OrgUnitKind.University);
    }

    /// <summary>يعرض الإصلاحات المعلقة؛ رمز الخروج 0 دائمًا (العرض لا يغير شيئًا).</summary>
    public static async Task<int> ListDataFixesAsync(IServiceProvider services, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var pending = await scope.ServiceProvider.GetRequiredService<Seeder>().PendingDataFixesAsync(CancellationToken.None);
        if (pending.Count == 0)
        {
            await output.WriteLineAsync("No data fixes are pending.");
            return 0;
        }
        await output.WriteLineAsync($"{pending.Count} data fix(es) await approval (apply with --apply-data-fixes --confirm --operator \"<name>\"):");
        foreach (var f in pending) await output.WriteLineAsync($"  - {f.Key}: {f.DescriptionEn} | {f.DescriptionAr}");
        return 0;
    }

    /// <summary>يطبق الإصلاحات المعلقة بعد التأكد من أن المخطط مرحل بالكامل.</summary>
    public static async Task<int> ApplyDataFixesAsync(IServiceProvider services, string approvedBy, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if ((await sp.GetRequiredService<Tier1DbContext>().Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("The database has pending migrations; run --migrate first.");
        var applied = await sp.GetRequiredService<Seeder>().ApplyDataFixesAsync(approvedBy, sp.GetRequiredService<IClock>().UtcNow, CancellationToken.None);
        await output.WriteLineAsync(applied.Count == 0 ? "No data fixes were pending." : $"Applied {applied.Count} data fix(es), approved by {approvedBy}:");
        foreach (var (fix, changed) in applied) await output.WriteLineAsync($"  - {fix.Key}: {changed} change(s)");
        return 0;
    }

    /// <summary>يسجل تحميل الهيكل الرسمي في سجل التدقيق باسم من وافق ونتيجته.</summary>
    public static async Task RecordStructureLoadAsync(IServiceProvider services, string approvedBy, string result)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IAuditLog>().Record(AuditEvents.Audit("org", "org.units", "seed.structure_loaded",
            "تحميل الهيكل الرسمي للجامعة", "Loaded the official university structure") with
        {
            ActorName = approvedBy,
            Target = new AuditTarget("structure", null, "university-structure.json"),
            Details = new { result },
        });
        await sp.GetRequiredService<Tier1DbContext>().SaveChangesAsync();
    }
}
