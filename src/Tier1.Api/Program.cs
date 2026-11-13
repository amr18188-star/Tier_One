using System.Globalization;
using Tier1.Api.Endpoints;
using Tier1.Api.Startup;
using Tier1.Infrastructure;
using Tier1.Infrastructure.Logging;
using Tier1.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ar-EG");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
});
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi(builder.Configuration);

var app = builder.Build();

// إعدادات التجربة مرفوضة في كل بيئة غير التطوير (الإنتاج والتجريب وغيرهما).
StartupGuards.Check(app.Configuration, app.Environment);

// أوامر التشغيل لمرة واحدة.
if (args.Contains("--migrate"))
{
    await DatabaseMigrator.MigrateAsync(app.Services, app.Configuration);
    return;
}
if (args.Contains("--mongo-setup"))
{
    await DatabaseMigrator.SetupMongoAsync(app.Configuration, app.Services.GetRequiredService<IOptions<MongoLogOptions>>().Value,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MongoSetup"), CancellationToken.None);
    return;
}
// يمسح بيانات المؤشرات كلها (التعريفات والمستهدفات والتعارضات والمبادرات والدورات والنماذج والقيم وأدلتها) ويحمل الإطار الشامل.
// تبقى الحسابات والأدوار والجهات والإعدادات وهياكل الكليات والسجلات. لا يعمل دون --confirm.
// قيد السجل المختوم يبقى في صندوق الصادر بالقاعدة ويكتب إلى السجل عند تشغيل الخادم التالي.
if (args.Contains("--reset-kpi"))
{
    if (!args.Contains("--confirm")) throw new ArgumentException("--reset-kpi deletes all indicator data. Add --confirm to proceed.");
    await DatabaseMigrator.MigrateAsync(app.Services, app.Configuration);
    await Console.Out.WriteLineAsync(await KpiFramework.ResetAsync(app.Services));
    return;
}
// يحمل هيكل الجامعة الرسمي (Seed/university-structure.json) ويحل محل الجهات التجريبية: تنقل نطاقات حساباتها ثم تحذف.
// لا يعمل دون --confirm.
if (args.Contains("--load-structure"))
{
    if (!args.Contains("--confirm")) throw new ArgumentException("--load-structure replaces the demo units. Add --confirm to proceed.");
    await DatabaseMigrator.MigrateAsync(app.Services, app.Configuration);
    await Console.Out.WriteLineAsync(await UniversityStructure.LoadAsync(app.Services));
    return;
}
// Full backup and restore test (replace the old PowerShell scripts).
if (args.Contains("--backup")) { Environment.ExitCode = await Maintenance.BackupAsync(args, app.Configuration); return; }
if (args.Contains("--restore-test")) { Environment.ExitCode = await Maintenance.RestoreTestAsync(args, app.Configuration); return; }
var createSa = Array.IndexOf(args, "--create-superadmin");
if (createSa >= 0)
{
    if (args.Length < createSa + 3) throw new ArgumentException("Usage: --create-superadmin <email> <arabic name>");
    await using var scope = app.Services.CreateAsyncScope();
    var temporary = await scope.ServiceProvider.GetRequiredService<Seeder>().CreateSuperAdminAsync(args[createSa + 1], args[createSa + 2], CancellationToken.None);
    // تطبع مرة واحدة على الطرفية فقط، ويلزم تغييرها عند أول دخول.
    await Console.Out.WriteLineAsync($"Temporary password for {args[createSa + 1]}: {temporary}");
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    await DatabaseMigrator.MigrateAsync(app.Services, app.Configuration);

app.UsePipeline();

var api = app.MapGroup("/api");
api.MapAuth();
api.MapIam();
api.MapLogs();
api.MapSettings();
api.MapTier1();
api.MapCollection();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource pg, CancellationToken ct) =>
    await DatabaseMigrator.CanConnectAsync(pg, ct) ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "unavailable" }, statusCode: 503)).AllowAnonymous();

app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
var spa = new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" };
app.MapFallbackToFile("index.html", spa).AllowAnonymous();
// أكواد المؤشرات فيها نقطة (1.01) فيظنها المسار العام ملفًا؛ صفحة المؤشر تعاد إلى الواجهة صراحة.
app.MapFallbackToFile("indicators/{code}", "index.html", spa).AllowAnonymous();

await app.RunAsync();

public partial class Program;
