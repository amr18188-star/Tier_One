using Tier1.Application.Access;
using Tier1.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tier1.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>ترحيلات PostgreSQL ثم البذر، ثم تهيئة مجموعات MongoDB وفهارسها إن طلبت.</summary>
    public static async Task MigrateAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrator");
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        await db.Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync(ct);
        // الإصدار المعروف لهذه النسخة يبدأ من القاعدة.
        var version = await db.AccessStates.AsNoTracking().Where(s => s.Id == 1).Select(s => s.Version).FirstAsync(ct);
        scope.ServiceProvider.GetRequiredService<AccessVersion>().Set(version);

        var options = scope.ServiceProvider.GetRequiredService<IOptions<MongoLogOptions>>().Value;
        if (options.SetupOnStartup) await SetupMongoAsync(config, options, log, ct);
    }

    public static async Task SetupMongoAsync(IConfiguration config, MongoLogOptions options, ILogger log, CancellationToken ct)
    {
        var admin = config.GetConnectionString("MongoAdmin");
        if (string.IsNullOrWhiteSpace(admin))
            throw new InvalidOperationException("ConnectionStrings:MongoAdmin is required to create the log collections and indexes.");
        await MongoSetup.RunAsync(admin, options, log, ct);
    }

    public static async Task<bool> CanConnectAsync(NpgsqlDataSource ds, CancellationToken ct)
    {
        try
        {
            await using var cmd = ds.CreateCommand("SELECT 1");
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }
}
