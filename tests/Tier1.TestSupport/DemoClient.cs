using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Tier1.TestSupport;

/// <summary>جلسة مستخدم تجريبي واحد على الخادم نفسه: كوكي الجلسة ورمز CSRF المرتبط بها (كما يفعل المتصفح).</summary>
internal sealed class DemoClient : IDisposable
{
    private readonly HttpClient http;
    private string? csrf;

    private DemoClient(HttpClient http, string email)
    {
        this.http = http;
        Email = email;
    }

    public string Email { get; }

    public static async Task<DemoClient> LoginAsync(Uri baseAddress, string email, string password, CancellationToken ct)
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        var http = new HttpClient(handler, disposeHandler: true) { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ar"));
        var client = new DemoClient(http, email);
        await client.CallAsync(HttpMethod.Post, "/api/auth/login", new JsonObject { ["email"] = email, ["password"] = password }, 200, ct);
        client.csrf = null; // الرمز مرتبط بالهوية؛ يجدد بعد الدخول.
        return client;
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (csrf is not null) return csrf;
        using var response = await http.GetAsync("/api/auth/csrf", ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return csrf = body?["token"]?.GetValue<string>() ?? throw new InvalidOperationException("No CSRF token.");
    }

    public async Task<(int Status, JsonNode? Body)> CallAsync(HttpMethod method, string path, JsonNode? body = null, int? expect = null, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            if (method != HttpMethod.Get) request.Headers.Add("X-XSRF-TOKEN", await TokenAsync(ct));
            using var response = await http.SendAsync(request, ct);
            // حد المعدل: ينتظر ثم يعيد المحاولة.
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 8)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                continue;
            }
            return await FinishAsync(method, path, response, expect, ct);
        }
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct) => (await CallAsync(HttpMethod.Get, path, null, 200, ct)).Body;

    public async Task<(int Status, JsonNode? Body)> UploadAsync(string path, string fileName, byte[] content, IReadOnlyDictionary<string, string>? fields, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields ?? new Dictionary<string, string>()) form.Add(new StringContent(v, Encoding.UTF8), k);
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", await TokenAsync(ct));
        using var response = await http.SendAsync(request, ct);
        return await FinishAsync(HttpMethod.Post, path, response, null, ct);
    }

    /// <summary>A binary download: status, body, content type and the response headers.</summary>
    public async Task<(int Status, byte[] Body, string ContentType, IReadOnlyDictionary<string, string> Headers)> DownloadAsync(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        var headers = response.Headers.Concat(response.Content.Headers).ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentType?.ToString() ?? "", headers);
    }

    private async Task<(int, JsonNode?)> FinishAsync(HttpMethod method, string path, HttpResponseMessage response, int? expect, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? parsed;
        try { parsed = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException) { parsed = new JsonObject { ["title"] = text }; }
        var status = (int)response.StatusCode;
        if (expect is { } want && status != want)
            throw new InvalidOperationException($"{Email} {method} {path}: expected {want}, got {status}: {text}");
        return (status, parsed);
    }

    /// <summary>
    /// A POST of JSON or a form, with extra request headers (e.g. Idempotency-Key) and this session's CSRF token; returns the
    /// response headers as well.
    /// </summary>
    public async Task<(int Status, JsonNode? Body, IReadOnlyDictionary<string, string> Headers)> PostWithHeadersAsync(
        string path, HttpContent content, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add("X-XSRF-TOKEN", await TokenAsync(ct));
        foreach (var (k, v) in headers) request.Headers.Add(k, v);
        using var response = await http.SendAsync(request, ct);
        var all = response.Headers.Concat(response.Content.Headers).ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var (status, body) = await FinishAsync(HttpMethod.Post, path, response, null, ct);
        return (status, body, all);
    }

    /// <summary>A request as built by the caller (custom headers, e.g. X-Passive), on this session's cookies.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => http.SendAsync(request, ct);

    public void Dispose() => http.Dispose();
}

internal static class Json
{
    public static string? Str(this JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    public static decimal? Num(this JsonNode? n) => n is JsonValue v && v.TryGetValue(out decimal d) ? d : null;

    public static bool Truthy(this JsonNode? n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out string? s) => !string.IsNullOrEmpty(s),
        JsonValue v when v.TryGetValue(out decimal d) => d != 0,
        _ => true,
    };

    public static IEnumerable<JsonNode> Items(this JsonNode? n) => n is JsonArray a ? a.Where(x => x is not null).Select(x => x!) : [];

    public static JsonNode? Copy(this JsonNode? n) => n?.DeepClone();
}
