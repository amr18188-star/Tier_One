using Tier1.Application;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Logs;
using Tier1.Infrastructure.Email;
using Tier1.Infrastructure.Logging;
using Tier1.Infrastructure.Persistence;
using Tier1.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Npgsql;

namespace Tier1.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddApplication(config);
        var pg = config.GetConnectionString("Tier1") is { Length: > 0 } p ? p : throw new InvalidOperationException("ConnectionStrings:Tier1 is required.");
        services.AddNpgsqlDataSource(pg);
        services.AddDbContext<Tier1DbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), x => x.MigrationsHistoryTable("__ef_migrations", "iam")).UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDb>(sp => sp.GetRequiredService<Tier1DbContext>());
        services.AddScoped<IIamLock, PostgresIamLock>();

        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<IQrCodes, QrCodes>();
        services.AddSingleton<IFileStore, Files.LocalFileStore>();
        services.AddSingleton<ISpreadsheets, Files.ClosedXmlSpreadsheets>();
        services.AddSingleton<IKpiWorkbook, Files.KpiWorkbook>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        var dp = services.AddDataProtection().SetApplicationName("Tier1");
        if (config["DataProtection:KeysPath"] is { Length: > 0 } keys) dp.PersistKeysToFileSystem(new DirectoryInfo(keys));
        // المفاتيح تفك أسرار التحقق بخطوتين، فتشفر في مكانها: بشهادة إن أعطيت، وإلا بحماية ويندوز للجهاز.
        if (config["DataProtection:CertificatePath"] is { Length: > 0 } cert)
            dp.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(cert, config["DataProtection:CertificatePassword"]));
        else if (OperatingSystem.IsWindows())
            dp.ProtectKeysWithDpapi(protectToLocalMachine: true);

        services.Configure<MongoLogOptions>(config.GetSection("MongoLogs"));
        var mongo = config.GetConnectionString("Mongo") is { Length: > 0 } m ? m : throw new InvalidOperationException("ConnectionStrings:Mongo is required.");
        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(mongo);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddSingleton<MongoLogContext>();
        services.AddSingleton<Health.ReadinessProbe>();
        services.AddSingleton<ChainKeyHolder>();
        services.AddScoped<CheckpointPublisher>();
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<MailSignal>();
        services.AddSingleton<Application.Notifications.IMailSignal>(sp => sp.GetRequiredService<MailSignal>());
        services.AddSingleton<Integration.EventSignal>();
        services.AddSingleton<Application.Integration.IEventSignal>(sp => sp.GetRequiredService<Integration.EventSignal>());
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<IOutboxSignal>(sp => sp.GetRequiredService<OutboxSignal>());
        services.AddSingleton<IOutboxDirectWriter, OutboxDirectWriter>();
        services.AddSingleton<TechLogWriter>();
        services.AddSingleton<ILogReader, MongoLogReader>();
        services.AddScoped<IChainVerifier, ChainVerifier>();

        services.AddScoped<Seeder>();

        // خطأ غير متوقع في مهمة خلفية يسجل ولا يوقف التطبيق كله؛ كل حلقة تعيد المحاولة بنفسها.
        services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        if (config.GetValue("BackgroundJobs:Enabled", true))
        {
            services.AddHostedService<OutboxDispatcher>();
            services.AddHostedService<TechLogPump>();
            services.AddHostedService<NightlyChainVerification>();
            services.AddHostedService<AccessVersionPoller>();
            services.AddHostedService<MailDispatcher>();
            services.AddHostedService<Integration.DomainEventDispatcher>();
            services.AddHostedService<RetentionJob>();
            services.AddHostedService<Email.FormReminderJob>();
        }
        return services;
    }
}

/// <summary>يقرأ رقم إصدار الصلاحيات العام كل خمس ثوانٍ، فيسري تعديل نسخة أخرى من التطبيق على هذه النسخة.</summary>
public sealed class AccessVersionPoller(NpgsqlDataSource dataSource, AccessVersion version, ILogger<AccessVersionPoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var cmd = dataSource.CreateCommand("SELECT version FROM iam.access_state WHERE id = 1");
                if (await cmd.ExecuteScalarAsync(stoppingToken) is long v) version.Set(v);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Could not read the permission version.");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
