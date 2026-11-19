using System.Net;
using Tier1.Domain.Common;
using Tier1.Infrastructure.Logging;

namespace Tier1.Api.Startup;

/// <summary>
/// شروط البدء: كل بيئة غير التطوير (الإنتاج والتجريب وما يسمى بغيرهما) ترفض إعدادات التجربة، والتطوير لا يستمع إلا على الجهاز نفسه،
/// فلا تكشف أدواته (الحسابات التجريبية وإيقاف التحقق بخطوتين) على الشبكة.
/// </summary>
public static class StartupGuards
{
    public static void Check(IConfiguration c, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            CheckLoopbackOnly(c);
            return;
        }
        Require(CairoTime.ZoneFound, "Time-zone data for Africa/Cairo is missing; deadlines and appointments would shift. Install tzdata.");
        Require(c.GetValue("Auth:RequireMfa", true), "Auth:RequireMfa=false is allowed in Development only.");
        Require(!c.GetValue<bool>("Seed:Demo"), "Seed:Demo must be false outside Development.");
        Require(!c.GetValue<bool>("Seed:DemoData"), "Seed:DemoData must be false outside Development.");
        Require(!c.GetValue<bool>("Seed:Flow"), "Seed:Flow must be false outside Development.");
        // D10 (ADR-0010): no automatic seeding outside Development; base data and fixes go through the approved commands.
        Require(!c.GetValue<bool>("Database:MigrateOnStartup"), "Database:MigrateOnStartup must be false outside Development; run --migrate as an approved step.");
        Require(!c.GetValue<bool>("Seed:Structure"), "Seed:Structure must be false outside Development; load the official structure with --load-structure --confirm --operator.");
        Require(!c.GetValue<bool>("Seed:AutoDataFixes"), "Seed:AutoDataFixes must be false outside Development; apply data fixes with --apply-data-fixes --confirm --operator.");
        Require(!string.IsNullOrWhiteSpace(c["DataProtection:KeysPath"]), "DataProtection:KeysPath is required outside Development.");
        // مفاتيح حماية البيانات تفك أسرار التحقق بخطوتين؛ لا تحفظ مكشوفة.
        Require(!string.IsNullOrWhiteSpace(c["DataProtection:CertificatePath"]) || OperatingSystem.IsWindows(),
            "DataProtection:CertificatePath is required outside Windows, so the key ring is encrypted at rest.");
        Require(c.GetValue("Cookies:Secure", true), "Cookies:Secure must be true outside Development.");
        foreach (var name in new[] { "Session", "Antiforgery" })
            Require((c[$"Cookies:{name}"] ?? "").StartsWith("__Host-", StringComparison.Ordinal), $"Cookies:{name} must use the __Host- prefix outside Development.");
        // التنبيهات ونقاط تثبيت السجلات ترسل بالبريد.
        Require(!string.IsNullOrWhiteSpace(c["Email:Host"]), "Email:Host is required outside Development (notifications and log checkpoints).");
        Require(!c.GetValue<bool>("MongoLogs:SetupOnStartup"), "MongoLogs:SetupOnStartup is for development. Run --mongo-setup once with the admin account.");
        Require(string.IsNullOrWhiteSpace(c.GetConnectionString("MongoAdmin")), "ConnectionStrings:MongoAdmin must not be configured on the running application.");
        Require((c.GetSection("Network:TrustedProxies").Get<string[]>() ?? []).Length > 0, "Network:TrustedProxies is required outside Development (the reverse proxy address).");
        Require(c.GetValue("RateLimiting:LoginPerMinute", 30) <= 60, "RateLimiting values look like development settings.");
        // الأدلة وقرارات التشكيل خارج مجلد التطبيق، في مسار مطلق يدخل النسخ الاحتياطي.
        Require(!string.IsNullOrWhiteSpace(c["Files:Root"]) && Path.IsPathFullyQualified(c["Files:Root"]!),
            "Files:Root must be an absolute path outside the application folder (evidence files are stored there).");
        // D7: بصمة السلسلة بمفتاح خارج القاعدتين، ونقاط التثبيت تنشر خارج المنظومة.
        Require(ChainKey.FromConfig(c["MongoLogs:ChainKey"]) is not null, "MongoLogs:ChainKey (>= 32 bytes, Base64) is required.");
        Require(!string.IsNullOrWhiteSpace(c["MongoLogs:CheckpointDirectory"]) || (c.GetSection("MongoLogs:CheckpointEmails").Get<string[]>() ?? []).Length > 0,
            "MongoLogs:CheckpointDirectory or MongoLogs:CheckpointEmails is required, so checkpoints are published outside the system.");
    }

    /// <summary>التطوير على عنوان الجهاز فقط (localhost أو 127.0.0.1 أو ::1).</summary>
    private static void CheckLoopbackOnly(IConfiguration c)
    {
        var urls = (c["urls"] ?? c["ASPNETCORE_URLS"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(c.GetSection("Kestrel:Endpoints").GetChildren().Select(e => e["Url"] ?? ""))
            .Where(u => u.Length > 0);
        foreach (var url in urls)
        {
            var host = url.Split("://", 2).Last().Split('/')[0];
            host = host.StartsWith('[') ? host[1..host.IndexOf(']')] : host.Split(':')[0];
            var loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
            Require(loopback, $"Development listens on loopback only; '{url}' would expose the demo accounts and the MFA switch. Use another environment name.");
        }
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
