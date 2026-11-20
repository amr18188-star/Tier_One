using System.Globalization;
using Tier1.Api.Endpoints;
using Tier1.Api.Startup;
using Tier1.Infrastructure;
using Tier1.Infrastructure.Persistence;

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

// Two one-off commands only (ARCH-4): migration and the first super admin. Data fixes, structure loading, backups, imports and
// demo data are in tools/Tier1.Tools, outside the production server.
// D10: --migrate applies the schema and base data only; pending data fixes are listed, and applied only by the approved tool command.
if (MovedCommands.Refusal(args) is { } moved)
{
    await Console.Error.WriteLineAsync(moved);
    Environment.ExitCode = 2;
    return;
}
if (args.Contains("--migrate"))
{
    await DatabaseMigrator.MigrateAsync(app.Services, app.Configuration);
    await DataFixReport.WriteAsync(app.Services, Console.Out);
    return;
}
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

app.MapGroup("/api").AddEndpointFilter<DefinedEnumsFilter>().MapApi();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
// PostgreSQL down is 503; Mongo, backlogs and failed notifications degrade the answer (200) and name the check.
app.MapGet("/health/ready", async (Tier1.Infrastructure.Health.ReadinessProbe probe, CancellationToken ct) =>
{
    var r = await probe.CheckAsync(ct);
    return Results.Json(r, statusCode: r.Status == "unavailable" ? 503 : 200);
}).AllowAnonymous();

app.MapFallback("/api/{**rest}", (HttpContext ctx) => ApiErrors.WriteAsync(ctx, 404, "not_found", Tier1.Domain.Common.Lang.Pick("المسار غير موجود.", "Not found."))).AllowAnonymous();
var spa = new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" };
app.MapFallbackToFile("index.html", spa).AllowAnonymous();
// أكواد المؤشرات فيها نقطة (1.01) فيظنها المسار العام ملفًا؛ صفحة المؤشر تعاد إلى الواجهة صراحة.
app.MapFallbackToFile("indicators/{code}", "index.html", spa).AllowAnonymous();

await app.RunAsync();

public partial class Program;
