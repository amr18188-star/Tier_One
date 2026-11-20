using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tier1.TestSupport;

namespace Tier1.Tools;

/// <summary>
/// <c>--demo-data</c>: realistic generated data on a development database, through the running API with each role's demo account
/// (validation, approvals, separation of duties and audit as in real use). It replaces the hosted service the API used to start
/// with <c>Seed:DemoData</c>. Runs once: when its cycles exist it skips. Development only.
/// </summary>
internal static class DemoData
{
    public static async Task<int> RunAsync(string[] args, IConfiguration config, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("--demo-data loads generated data through the API with the demo accounts; it runs in Development only (DOTNET_ENVIRONMENT=Development).");
        var server = new Uri(Maintenance.Option(args, "--base") ?? "http://localhost:5150");
        var password = Maintenance.Option(args, "--password") ?? config["Seed:DemoPassword"]
            ?? throw new ArgumentException("No demo password: pass --password or set Seed:DemoPassword.");
        var wait = TimeSpan.FromSeconds(int.Parse(Maintenance.Option(args, "--wait") ?? "300", CultureInfo.InvariantCulture));

        using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var log = loggers.CreateLogger("DemoData");
        await WaitForServerAsync(server, wait, log);
        using var run = new DemoRun(server, password, log);
        await run.ExecuteAsync(CancellationToken.None);
        return 0;
    }

    /// <summary>The API may still be building or migrating (dotnet watch in the dev container): waits for /health/live.</summary>
    internal static async Task WaitForServerAsync(Uri server, TimeSpan wait, ILogger log)
    {
        using var handler = new HttpClientHandler { CheckCertificateRevocationList = true };
        using var http = new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(5) };
        var until = DateTime.UtcNow + wait;
        var told = false;
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(new Uri("/health/live", UriKind.Relative));
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            if (DateTime.UtcNow > until) throw new InvalidOperationException($"No server answered at {server} within {wait.TotalSeconds:0} seconds.");
            if (!told) log.LogInformation("Waiting for the API at {Server}...", server);
            told = true;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
