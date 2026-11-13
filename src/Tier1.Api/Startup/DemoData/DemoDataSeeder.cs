#pragma warning disable CA5394 // بيانات تجريبية مولدة: العشوائية هنا ليست لغرض أمني.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Tier1.Api.Startup.DemoData;

/// <summary>
/// بيانات تجريبية واقعية على قاعدة التطوير (<c>Seed:DemoData=true</c>). تدخل عبر واجهة البرنامج نفسها بحساب كل دور،
/// فتمر بقواعد التحقق والاعتماد وفصل المهام وتسجل في سجلات التدقيق كأي استخدام فعلي:
/// هياكل Tier 1 بالكليات الثلاث (بشواغر في التجارة)، ثلاث دورات ربعية (ربعان مغلقان ومنقولان إلى مؤشرات الجامعة،
/// وربع حالي بنماذج في كل مراحل الاعتماد). كل القيم والأسماء مولدة وليست بيانات الجامعة.
/// تعمل مرة واحدة: إن وجدت دوراتها تتخطى.
/// </summary>
internal sealed class DemoDataSeeder(IServer server, IHostApplicationLifetime lifetime, IConfiguration config, ILogger<DemoDataSeeder> log) : BackgroundService
{
    private static readonly TimeSpan StartupWait = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var started = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            started.CancelAfter(StartupWait);
            var ready = new TaskCompletionSource();
            await using (lifetime.ApplicationStarted.Register(() => ready.TrySetResult()))
                await ready.Task.WaitAsync(started.Token);

            var password = config["Seed:DemoPassword"] ?? throw new InvalidOperationException("Seed:DemoPassword is required for Seed:DemoData.");
            var run = new DemoRun(BaseAddress(), password, log);
            await run.ExecuteAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // فشل البيانات التجريبية لا يوقف الخادم.
            log.LogError(e, "Demo data seeding failed.");
        }
    }

    /// <summary>عنوان الخادم نفسه من الجهة المحلية، حتى لو استمع على كل العناوين.</summary>
    private Uri BaseAddress()
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The server has no http address to seed through.");
        var uri = new Uri(address.Replace("[::]", "127.0.0.1", StringComparison.Ordinal).Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)
            .Replace("://+", "://127.0.0.1", StringComparison.Ordinal).Replace("://*", "://127.0.0.1", StringComparison.Ordinal));
        return uri;
    }
}

/// <summary>مولد عشوائي ثابت بمفتاح نصي: القيم نفسها في كل تشغيل.</summary>
internal sealed class Rnd(params object[] keys)
{
    private readonly Random random = new(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', keys))), 0));

    public double Uniform(double a, double b) => a + (b - a) * random.NextDouble();

    public int Int(int a, int b) => random.Next(a, b + 1);

    public double Next() => random.NextDouble();

    public T Choice<T>(params T[] items) => items[random.Next(items.Length)];

    public string Weighted(string[] items, int[] weights)
    {
        var x = random.NextDouble() * weights.Sum();
        for (var i = 0; i < items.Length; i++)
        {
            x -= weights[i];
            if (x < 0) return items[i];
        }
        return items[^1];
    }
}
