using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Application.Logs;

namespace Tier1.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        // القيم الرقمية تفحص عند الإقلاع: قيمة خاطئة توقف التشغيل برسالة واضحة بدل قفل أو مهلة غريبة لاحقًا.
        services.AddOptions<AuthOptions>().Bind(config.GetSection("Auth")).ValidateOnStart()
            .Validate(o => o.IdleMinutes is > 0 and <= 480 && o.AbsoluteHours is > 0 and <= 72, "Auth: IdleMinutes 1-480 and AbsoluteHours 1-72.")
            .Validate(o => o.MaxFailedLogins is > 0 and <= 20 && o.MaxFailedLoginsPerAccount >= o.MaxFailedLogins && o.LockoutMinutes is > 0 and <= 1440,
                "Auth: MaxFailedLogins 1-20, MaxFailedLoginsPerAccount >= it, LockoutMinutes 1-1440.");

        // المنصة: الصلاحيات والدخول والسجلات والإعدادات.
        services.AddMemoryCache();
        services.AddSingleton<AccessVersion>();
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IAccessCache>(sp => sp.GetRequiredService<IAccessService>());
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<SignInService>();
        services.AddScoped<MeService>();
        services.AddScoped<RbacAdminService>();
        services.AddScoped<UsersAdminService>();
        services.AddScoped<IamPolicy>();
        services.AddScoped<UserAccountsService>();
        services.AddScoped<RoleAssignmentService>();
        services.AddScoped<UserOverridesService>();
        services.AddScoped<LogsService>();
        services.AddScoped<Settings.SettingsService>();
        services.AddScoped<IMailOutbox, Notifications.MailOutbox>();
        services.AddScoped<Integration.IEventOutbox, Integration.EventOutbox>();
        services.AddScoped<Notifications.NotificationPublisher>();
        services.AddScoped<Notifications.INotificationPublisher>(sp => sp.GetRequiredService<Notifications.NotificationPublisher>());
        services.AddScoped<Notifications.InboxService>();
        services.AddScoped<Notifications.MailAdminService>();

        // Tier 1: الجهات والهيكل، والقاموس والمستهدفات والتعارضات، والصفحة الأولى.
        services.AddScoped<Organization.OrgUnitsService>();
        services.AddScoped<Organization.UnitIndicatorsService>();
        services.AddScoped<Structure.StructureService>();
        services.AddScoped<Kpi.DictionaryService>();
        services.AddScoped<Kpi.TargetsService>();
        services.AddScoped<Kpi.StrategyService>();
        services.AddScoped<Kpi.ConflictsService>();
        services.AddScoped<Home.HomeService>();

        // جمع البيانات: الدورات والنماذج ومسار الاعتماد والأدلة والنتائج.
        services.AddScoped<Collection.FormNotifier>();
        services.AddScoped<Integration.IEventHandler, Collection.FormMovedNotifications>();
        services.AddScoped<Collection.CyclesService>();
        services.AddScoped<Collection.FormReader>();
        services.AddScoped<Collection.FormReader>();
        services.AddScoped<Collection.FormsService>();
        services.AddScoped<Collection.FormEntryService>();
        services.AddScoped<Collection.FormWorkflowService>();
        services.AddScoped<Collection.FormEntryService>();
        services.AddScoped<Collection.FormWorkflowService>();
        services.AddScoped<Collection.DocumentsService>();
        services.AddScoped<Collection.ResultsService>();
        services.AddSingleton<Collection.CycleSnapshotCache>();

        // بطاقة أداء الجامعة وفئتها بمعادلات الإطار الشامل.
        services.AddScoped<Scorecard.ScorecardService>();
        services.AddScoped<Scorecard.ReviewService>();
        services.AddScoped<Scorecard.ProgressService>();
        return services;
    }
}
