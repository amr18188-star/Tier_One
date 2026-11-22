using System.Net.Http;
using System.Text.Json.Nodes;
using Tier1.TestSupport;
using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// End-to-end tests against a RUNNING server (replace the old Python tests/e2e scripts).
/// Server: <c>T1_BASE</c> (default http://localhost:5150). Demo password: <c>T1_DEMO_PASSWORD</c> (default Tier1-Demo-Pass1).
/// They skip themselves when no server answers, so a plain <c>dotnet test</c> never fails for lack of one.
/// </summary>
internal static class Env
{
    public static readonly Uri Base = new(Environment.GetEnvironmentVariable("T1_BASE") ?? "http://localhost:5150");
    public static readonly string Password = Environment.GetEnvironmentVariable("T1_DEMO_PASSWORD") ?? "Tier1-Demo-Pass1";

    private static readonly Lazy<bool> Reachable = new(() =>
    {
        try
        {
            using var http = new HttpClient { BaseAddress = Base, Timeout = TimeSpan.FromSeconds(3) };
            return http.GetAsync("/health/live").GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    });

    public static bool ServerUp => Reachable.Value;

    private static readonly Dictionary<string, DemoClient> Clients = [];

    public static async Task<DemoClient> As(string name)
    {
        if (!Clients.TryGetValue(name, out var client))
            Clients[name] = client = await DemoClient.LoginAsync(Base, $"{name}@tier1.local", Password, CancellationToken.None);
        return client;
    }

    // On the official structure the demo colleges are the real units with these codes; tests keep using the demo codes.
    private static readonly Dictionary<string, string> OfficialToDemo = new()
    {
        ["F06"] = "ENG", ["F06-D01"] = "ENG-CIV", ["F06-D02"] = "ENG-ELE", ["F06-D03"] = "ENG-MEC", ["F08"] = "CS", ["F08-D01"] = "CS-SE", ["F08-D04"] = "CS-AI",
        ["F17"] = "COM", ["F17-D01"] = "COM-ACC", ["F17-D02"] = "COM-BUS", ["SPU"] = "QAC", ["A06"] = "STU", ["ICT"] = "DIG", ["A11"] = "INTL",
    };

    /// <summary>The unit directory keyed by demo code, whichever structure is loaded (codes are renamed in memory only).</summary>
    public static async Task<Dictionary<string, JsonNode>> UnitsAsync(DemoClient client)
    {
        var list = (await client.GetAsync("/api/org/units", default)).Items().ToList();
        if (list.Any(u => u["code"].Str() == "F06"))
            foreach (var u in list.Where(u => OfficialToDemo.ContainsKey(u["code"].Str()!))) u["code"] = OfficialToDemo[u["code"].Str()!];
        return list.ToDictionary(u => u["code"].Str()!);
    }

    /// <summary>The repository root, found from this source file's location (the test host runs from an output folder).</summary>
    public static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(here)!); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    public static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    /// <summary>A PDF with a unique comment: the same file attached twice to one owner is refused (BE-8), and tests run again on the same data.</summary>
    public static byte[] UniquePdf() => [.. Pdf, .. System.Text.Encoding.ASCII.GetBytes($"% {Guid.NewGuid():N}\n")];
}

/// <summary>
/// Skips when no server is reachable (set T1_BASE to point at one). With <c>T1_E2E_REQUIRE_SERVER=1</c> (CI) it never skips,
/// so a missing server fails the run instead of passing green.
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!Env.ServerUp && Environment.GetEnvironmentVariable("T1_E2E_REQUIRE_SERVER") != "1")
            Skip = $"No server at {Env.Base}; start one (docker compose ... up) or set T1_BASE.";
    }
}

/// <summary>Collects every check of a scenario so one failure does not hide the rest, like the old scripts' ✓/✗ lists.</summary>
internal sealed class Scenario(ITestOutputHelper output)
{
    private readonly List<string> failures = [];
    private int total;

    public void Step(string title) => output.WriteLine("\n" + title);

    public void Check(bool ok, string name)
    {
        total++;
        output.WriteLine((ok ? "  ✓ " : "  ✗ ") + name);
        if (!ok) failures.Add(name);
    }

    public void Finish() => Assert.True(failures.Count == 0, $"{failures.Count}/{total} checks failed:\n - " + string.Join("\n - ", failures));

    public static JsonObject O(params (string Key, object? Value)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs)
            o[k] = v switch
            {
                null => null, JsonNode n => n.DeepClone(), string s => s, int i => i, long l => l, double d => d, decimal m => m, bool b => b,
                _ => throw new ArgumentException($"Unsupported value for {k}: {v.GetType()}"),
            };
        return o;
    }

    public static JsonObject With(JsonNode source, params (string Key, object? Value)[] pairs)
    {
        var o = (JsonObject)source.DeepClone();
        foreach (var (k, v) in O(pairs).ToList()) { o.Remove(k); o[k] = v?.DeepClone(); }
        return o;
    }
}

internal static class Calls
{
    public static Task<(int Status, JsonNode? Body)> Call(this DemoClient c, string method, string path, JsonNode? body = null, int? expect = null) =>
        c.CallAsync(new HttpMethod(method), path, body, expect, default);

    public static Task<JsonNode?> Get(this DemoClient c, string path) => c.GetAsync(path, default);

    public static async Task<JsonNode> Ok(this DemoClient c, string method, string path, JsonNode? body = null, int expect = 200) =>
        (await c.CallAsync(new HttpMethod(method), path, body, expect, default)).Body ?? new JsonObject();

    public static Task<(int Status, JsonNode? Body)> Upload(this DemoClient c, string path, string name, byte[] content, params (string K, string V)[] fields) =>
        c.UploadAsync(path, name, content, fields.ToDictionary(f => f.K, f => f.V), default);

    public static Task<(int Status, byte[] Body, string ContentType, IReadOnlyDictionary<string, string> Headers)> Download(this DemoClient c, string path) => c.DownloadAsync(path, default);

    public static string Code(this JsonNode? error) => error?["code"].Str() ?? "";

    /// <summary>BE-2: every form transition carries the version of the form it acts on; this reads the current one into the body.</summary>
    public static async Task<JsonObject> Versioned(this DemoClient c, string formPath, JsonObject? body = null)
    {
        var form = (await c.GetAsync(formPath, default))!;
        var o = body is null ? new JsonObject() : (JsonObject)body.DeepClone();
        o["version"] = form["version"]?.DeepClone();
        return o;
    }

    public static string Today(int plusDays = 0) => DateOnly.FromDateTime(DateTime.Today).AddDays(plusDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string Tag(int length = 6) => Guid.NewGuid().ToString("N")[..length];

    public static IEnumerable<JsonNode> ItemsOf(this JsonNode form) => form["sections"].Items().SelectMany(s => s["items"].Items());

    /// <summary>A valid value for an item, by its unit and aggregation rule.</summary>
    public static JsonObject InputFor(JsonNode item)
    {
        var body = Scenario.O(("indicatorId", item["indicatorId"]), ("kind", "Number"), ("value", null), ("numerator", null), ("denominator", null),
            ("reason", null), ("explanation", null), ("version", item["value"].Truthy() ? item["value"]!["version"] : null));
        if (item["usesRatio"].Truthy()) { body["numerator"] = 45; body["denominator"] = 50; }
        else if (item["unit"].Str()?.Trim() == "%") body["value"] = 80;
        else body["value"] = 12;
        if (item["aggregation"].Str() == "WeightedAverage") body["denominator"] = 30; // a weighted mean needs its weight
        return body;
    }

    /// <summary>Enters every indicator of a form and attaches the required evidence.</summary>
    public static async Task<JsonNode> Fill(this DemoClient entry, JsonNode form, string note = "")
    {
        var id = form["id"].Str();
        // The caller's copy may be older than values saved since (each save moves the value's version), and a stale version is
        // refused with 409: read the form again, and fail on any refusal instead of leaving an indicator as it was.
        form = (await entry.Get($"/api/forms/{id}"))!;
        foreach (var item in form.ItemsOf().ToList())
        {
            var body = InputFor(item);
            var (status, saved) = await entry.Call("PUT", $"/api/forms/{id}/values", body);
            if (status != 200) throw new InvalidOperationException($"{item["code"].Str()}: {status} {saved}");
            if (saved!["issues"].Items().Any(i => i["code"].Str() == "big_change"))
            {
                body["explanation"] = "تغير متوقع بعد إعادة هيكلة البرامج " + note;
                body["version"] = saved["value"]!["version"].Copy();
                saved = await entry.Ok("PUT", $"/api/forms/{id}/values", body);
            }
            if (item["evidenceRequired"].Truthy() && !saved["evidence"].Items().Any(e => !e["voided"].Truthy()))
                await entry.Upload($"/api/forms/{id}/values/{item["indicatorId"].Str()}/evidence", "evidence.pdf", Env.Pdf, ("description", "كشف من شؤون الطلاب"));
        }
        return (await entry.Get($"/api/forms/{id}"))!;
    }
}
