using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tier1.Infrastructure;
using Tier1.Infrastructure.Logging;
using Tier1.Infrastructure.Persistence;
using Tier1.Tools;

// ARCH-4: the operations, import and demo-data commands, apart from the production API. Each command runs once and exits;
// no background job starts. The API itself keeps only --migrate and --create-superadmin.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ar-EG");

const string Usage = """
    Tier1.Tools <command> [options]     (dotnet run --project tools/Tier1.Tools -- <command>, or dotnet Tier1.Tools.dll <command>)

    Data (ADR-0010):
      --data-fixes                                         list the pending data fixes
      --apply-data-fixes --confirm --operator "<name>"     apply them, each recorded in the audit log
      --load-structure --confirm --operator "<name>"       load the official university structure (Production: first setup only)
      --reset-kpi --confirm                                delete all indicator data and load the framework (refused in Production)
      --mongo-setup                                        create the log collections and indexes (needs ConnectionStrings:MongoAdmin)
    Backup:
      --backup <folder> [--keep-days 30] [--pg-bin <dir>] [--mongo-uri <uri>]
      --restore-test <backup folder> [--pg-bin <dir>]
    Seed extraction from the source workbooks:
      --import-structure <xlsx> [--repo-root <dir>]        writes Seed/university-structure.json
      --import-kpi <xlsx> [--repo-root <dir>]              writes Seed/kpi-framework.json
    Development only:
      --demo-data [--base http://localhost:5150] [--password <demo password>] [--wait 300]
                                                           generated cycles, forms and values through the running API

    Settings come from the API's appsettings files, user secrets (Development) and environment variables, as for the server.
    The environment is DOTNET_ENVIRONMENT, else ASPNETCORE_ENVIRONMENT, else Production.
    """;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production,
});
// A command never starts the server's background jobs (outbox, mail, retention...): they belong to the running API.
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["BackgroundJobs:Enabled"] = "false" });
var config = builder.Configuration;
var production = builder.Environment.IsProduction();

try
{
    return await RunAsync();
}
catch (Exception e) when (e is ArgumentException or InvalidOperationException)
{
    await Console.Error.WriteLineAsync($"error: {e.Message}");
    return 2;
}

async Task<int> RunAsync()
{
    // Commands that need only the configuration.
    if (args.Contains("--backup")) return await Maintenance.BackupAsync(args, config);
    if (args.Contains("--restore-test")) return await Maintenance.RestoreTestAsync(args, config);
    if (args.Contains("--import-structure") || args.Contains("--import-kpi"))
    {
        var kpi = args.Contains("--import-kpi");
        var workbook = Maintenance.Option(args, kpi ? "--import-kpi" : "--import-structure")
            ?? throw new ArgumentException("Usage: --import-structure <xlsx> | --import-kpi <xlsx> [--repo-root <dir>]");
        var repoRoot = Maintenance.Option(args, "--repo-root") is { } chosen ? Path.GetFullPath(chosen) : SeedImporters.FindRepoRoot();
        await Console.Out.WriteLineAsync(kpi ? SeedImporters.ImportKpi(workbook, repoRoot) : SeedImporters.ImportStructure(workbook, repoRoot));
        return 0;
    }
    if (args.Contains("--demo-data")) return await DemoData.RunAsync(args, config, builder.Environment);

    // Commands on the databases: the server's own services, built but never run.
    builder.Services.AddInfrastructure(config);
    using var host = builder.Build();
    var services = host.Services;

    if (args.Contains("--data-fixes")) return await DataFixReport.WriteAsync(services, Console.Out);
    if (args.Contains("--apply-data-fixes"))
    {
        if (!args.Contains("--confirm"))
            throw new ArgumentException("--apply-data-fixes changes role permissions in the database. Review --data-fixes, then add --confirm --operator \"<name>\".");
        return await SeedCommands.ApplyDataFixesAsync(services, SeedCommands.Operator(args, production, "--apply-data-fixes"), Console.Out);
    }
    if (args.Contains("--mongo-setup"))
    {
        await DatabaseMigrator.SetupMongoAsync(config, services.GetRequiredService<IOptions<MongoLogOptions>>().Value,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("MongoSetup"), CancellationToken.None);
        return 0;
    }
    // Deletes all indicator data (definitions, targets, conflicts, initiatives, cycles, forms, values and their evidence) and loads
    // the comprehensive framework. Accounts, roles, units, settings, college structures and logs stay. Never without --confirm.
    // The sealed log entry stays in the database outbox and reaches the log when the server next runs.
    if (args.Contains("--reset-kpi"))
    {
        if (SeedCommands.Refusal("--reset-kpi", production, 0) is { } refused) throw new InvalidOperationException(refused);
        if (!args.Contains("--confirm")) throw new ArgumentException("--reset-kpi deletes all indicator data. Add --confirm to proceed.");
        await DatabaseMigrator.MigrateAsync(services, config);
        await Console.Out.WriteLineAsync(await KpiFramework.ResetAsync(services));
        return 0;
    }
    // Loads the official university structure (Seed/university-structure.json) in place of the demo units: their accounts'
    // scopes move to the official units, then the demo units are deleted. Never without --confirm.
    if (args.Contains("--load-structure"))
    {
        if (!args.Contains("--confirm")) throw new ArgumentException("--load-structure replaces the demo units. Add --confirm to proceed.");
        var approvedBy = SeedCommands.Operator(args, production, "--load-structure");
        await DatabaseMigrator.MigrateAsync(services, config);
        if (SeedCommands.Refusal("--load-structure", production, await SeedCommands.ExistingUnitsAsync(services)) is { } refused)
            throw new InvalidOperationException(refused);
        var loaded = await UniversityStructure.LoadAsync(services);
        await SeedCommands.RecordStructureLoadAsync(services, approvedBy, loaded);
        await Console.Out.WriteLineAsync(loaded);
        return 0;
    }

    await Console.Error.WriteLineAsync($"Unknown command: {string.Join(' ', args)}\n\n{Usage}");
    return 1;
}
