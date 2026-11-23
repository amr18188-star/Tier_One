using System.Net;
using System.Net.Http.Json;

namespace Tier1.IntegrationTests;

/// <summary>
/// ADR-0016 against the real application: approved university email domains (managed in the database, subdomains included) and
/// the global and per-user IP allowlists, enforced at sign-in and on every request. Addresses are simulated with X-Forwarded-For
/// (the test host trusts 127.0.0.1 as a proxy); the administrators below work from 127.0.0.1 itself.
/// </summary>
[Collection("app")]
public sealed class UniversityAccessTests(Tier1App app)
{
    /// <summary>A browser from one address: cookies, a CSRF token, and X-Forwarded-For on every request.</summary>
    private sealed class Browser : IDisposable
    {
        private readonly HttpClient http;
        private string? csrf;

        public Browser(Uri server, string ip)
        {
            http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, CheckCertificateRevocationList = true }) { BaseAddress = server };
            Ip = ip;
        }

        public string Ip { get; set; }

        public async Task<(int Status, JsonNode? Body)> SendAsync(HttpMethod method, string path, object? body = null)
        {
            if (method != HttpMethod.Get && csrf is null)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
                get.Headers.Add("X-Forwarded-For", Ip);
                using var r = await http.SendAsync(get);
                csrf = JsonNode.Parse(await r.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
            }
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Forwarded-For", Ip);
            if (csrf is not null) request.Headers.Add("X-XSRF-TOKEN", csrf);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, text.Length > 0 ? JsonNode.Parse(text) : null);
        }

        /// <summary>Signs in; the CSRF token is bound to the identity, so a new one is fetched after.</summary>
        public async Task<(int Status, string? Code)> SignInAsync(string email, string password)
        {
            var (status, body) = await SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password });
            csrf = null;
            return (status, body?["code"].Str());
        }

        public void Dispose() => http.Dispose();
    }

    private static (string? Field, string? Code) FirstError(JsonNode? error) =>
        error?["errors"]?.AsArray().FirstOrDefault() is { } e ? (e["field"].Str(), e["code"].Str()) : (null, null);

    private async Task<(int Status, JsonNode? Body)> CreateUserAsync(string email)
    {
        var sys = await app.As("sysadmin");
        return await sys.Call("POST", "/api/iam/users", Calls.Account(email, "حساب اختبار النطاقات"));
    }

    private Task<long> OutboxAsync(string action, string like) =>
        app.CountAsync("SELECT count(*) FROM logs.outbox WHERE payload::text LIKE @a AND payload::text LIKE @l", ("a", $"%\"{action}\"%"), ("l", $"%{like}%"));

    [Fact]
    public async Task University_domains_are_data_that_cover_their_subdomains_and_only_them()
    {
        var sys = await app.As("sysadmin");
        var tag = Calls.Tag();
        var list = (await sys.Get("/api/security/domains"))!.AsArray();
        // The two universities come from the migration, as rows the administrator manages.
        Assert.Contains(list, d => d!["domain"].Str() == "capu.edu.eg" && d["active"]!.GetValue<bool>() && d["universityEn"].Str() == "Capital University");
        Assert.Contains(list, d => d!["domain"].Str() == "helwan.edu.eg" && d["active"]!.GetValue<bool>());

        foreach (var email in new[] { $"a{tag}@capu.edu.eg", $"b{tag}@eng.capu.edu.eg", $"c{tag}@commerce.capu.edu.eg", $"d{tag}@helwan.edu.eg", $"e{tag}@sub.helwan.edu.eg" })
            Assert.True((await CreateUserAsync(email)).Status == 200, email);
        foreach (var email in new[] { $"f{tag}@gmail.com", $"g{tag}@xcapu.edu.eg", $"h{tag}@capu.edu.eg.evil.com", $"i{tag}@alex.edu.eg" })
        {
            var (status, error) = await CreateUserAsync(email);
            Assert.Equal((400, "email_domain_not_allowed"), (status, error.Code()));
            Assert.Equal(("email", "email_domain_not_allowed"), FirstError(error));
        }
        // Duplicates are refused whatever the case.
        Assert.Equal(409, (await CreateUserAsync($"A{tag}@CAPU.edu.eg")).Status);

        // A new university is a setting: its subdomains are accepted at once, with no deployment.
        var root = $"alex{tag}.edu.eg";
        var (added, domain) = await sys.Call("POST", "/api/security/domains", Scenario.O(("domain", " @" + root.ToUpperInvariant()), ("universityAr", "جامعة الإسكندرية"), ("universityEn", "Alexandria University"), ("active", true), ("version", null)));
        Assert.Equal(200, added);
        Assert.Equal(root, domain!["domain"].Str());
        var id = domain["id"].Str()!;
        try
        {
            var (created, user) = await CreateUserAsync($"j{tag}@med.{root}");
            Assert.Equal(200, created);
            var (againStatus, again) = await sys.Call("POST", "/api/security/domains", Scenario.O(("domain", root), ("universityAr", "مكرر"), ("universityEn", null), ("active", true), ("version", null)));
            Assert.Equal((409, "duplicate_domain"), (againStatus, again.Code()));
            Assert.Equal("domain_covered", (await sys.Call("POST", "/api/security/domains", Scenario.O(("domain", "eng." + root), ("universityAr", "فرعي"), ("universityEn", null), ("active", true), ("version", null)))).Body.Code());
            Assert.Equal("invalid_domain", (await sys.Call("POST", "/api/security/domains", Scenario.O(("domain", "alex"), ("universityAr", "خطأ"), ("universityEn", null), ("active", true), ("version", null)))).Body.Code());

            // Disabling the domain: the list says how many accounts it has; no new account there; the existing one keeps signing in.
            domain = (await sys.Get("/api/security/domains"))!.AsArray().Single(d => d!["id"].Str() == id)!;
            Assert.Equal(1, domain["users"]!.GetValue<int>());
            var (disabled, _) = await sys.Call("PUT", $"/api/security/domains/{id}", Scenario.O(("domain", root), ("universityAr", "جامعة الإسكندرية"), ("universityEn", "Alexandria University"), ("active", false), ("version", domain["version"])));
            Assert.Equal(200, disabled);
            Assert.Equal("email_domain_not_allowed", (await CreateUserAsync($"k{tag}@{root}")).Body.Code());
            using (var browser = new Browser(app.Base, "10.20.30.40"))
                Assert.Equal(200, (await browser.SignInAsync($"j{tag}@med.{root}", user!["temporaryPassword"].Str()!)).Status);

            // An email change follows the same rule.
            var userId = user["userId"].Str();
            var (bad, badError) = await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.O(("fullNameAr", "حساب اختبار النطاقات"), ("fullNameEn", "Test Account"), ("phone", "01000000001"), ("email", $"j{tag}@gmail.com")));
            Assert.Equal((400, "email"), (bad, FirstError(badError).Field));
            Assert.Equal(204, (await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.O(("fullNameAr", "حساب اختبار النطاقات"), ("fullNameEn", "Test Account"), ("phone", "01000000001"), ("email", $"j{tag}@eng.helwan.edu.eg")))).Status);
            Assert.Equal($"j{tag}@eng.helwan.edu.eg", (await sys.Get($"/api/iam/users/{userId}"))!["email"].Str());

            // Only the security permission manages domains.
            Assert.Equal(403, (await (await app.As("planner")).Call("GET", "/api/security/domains")).Status);
            Assert.Equal(403, (await (await app.As("president")).Call("POST", "/api/security/domains", Scenario.O(("domain", "x.edu.eg"), ("universityAr", "س"), ("universityEn", null), ("active", true), ("version", null)))).Status);
        }
        finally
        {
            Assert.Equal(204, (await sys.Call("POST", $"/api/security/domains/{id}/delete", Scenario.O(("reason", "انتهى اختبار النطاقات")))).Status);
        }
        Assert.Equal(1, await OutboxAsync("security.domain_add", root));
        Assert.Equal(1, await OutboxAsync("security.domain_disable", root));
        Assert.Equal(1, await OutboxAsync("security.domain_delete", root));
    }

    [Fact]
    public async Task Global_and_individual_ip_allowlists_work_together_at_sign_in_and_on_every_request()
    {
        var sys = await app.As("sysadmin");
        var tag = Calls.Tag();
        var (created, user) = await CreateUserAsync($"ip{tag}@capu.edu.eg");
        Assert.Equal(200, created);
        var userId = user!["userId"].Str()!;
        var email = $"ip{tag}@capu.edu.eg";
        var password = user["temporaryPassword"].Str()!;

        // Validation before saving, with the field to mark.
        var (invalid, invalidError) = await sys.Call("POST", "/api/security/ip-allowlist/entries", Scenario.O(("value", "300.1.1.1"), ("label", null), ("version", null)));
        Assert.Equal((400, "invalid_ip", "value"), (invalid, invalidError.Code(), FirstError(invalidError).Field));
        Assert.Equal(403, (await (await app.As("planner")).Call("GET", "/api/security/ip-allowlist")).Status);

        try
        {
            // The campus range, and the administrators' own address; the global list is still off.
            var campus = await sys.Ok("POST", "/api/security/ip-allowlist/entries", Scenario.O(("value", "41.65.100.0/24"), ("label", "شبكة الحرم"), ("version", null)));
            Assert.Equal("41.65.100.0/24", campus["entries"]!.AsArray().Last()!["value"].Str());
            Assert.Equal(409, (await sys.Call("POST", "/api/security/ip-allowlist/entries", Scenario.O(("value", "41.65.100.0/24"), ("label", null), ("version", null)))).Status);

            // Turning it on without the administrator's own address would shut them out: refused.
            var (lockout, lockoutError) = await sys.Call("PUT", "/api/security/ip-allowlist/enabled", Scenario.O(("enabled", true), ("reason", "تشغيل قيد الشبكة")));
            Assert.Equal((422, "ip_lockout"), (lockout, lockoutError.Code()));
            var mine = await sys.Ok("POST", "/api/security/ip-allowlist/entries", Scenario.O(("value", "127.0.0.1"), ("label", "خادم الاختبار"), ("version", null)));
            var on = await sys.Ok("PUT", "/api/security/ip-allowlist/enabled", Scenario.O(("enabled", true), ("reason", "تشغيل قيد الشبكة")));
            Assert.True(on["enabled"]!.GetValue<bool>());

            // Global only: the campus is allowed, anywhere else is not, and the refusal comes only after the right password.
            using (var campusBrowser = new Browser(app.Base, "41.65.100.9"))
                Assert.Equal(200, (await campusBrowser.SignInAsync(email, password)).Status);
            using (var outside = new Browser(app.Base, "197.50.1.7"))
            {
                Assert.Equal((401, "invalid_credentials"), await outside.SignInAsync(email, "Not-The-Password-1"));
                Assert.Equal((403, "ip_not_allowed"), await outside.SignInAsync(email, password));
            }
            Assert.Equal(1, await app.CountAsync("SELECT count(*) FROM iam.login_attempts WHERE user_id = @u AND ip = '197.50.1.7' AND outcome = 'blocked' AND reason = 'ip_not_allowed'", ("u", Guid.Parse(userId))));
            Assert.True(await OutboxAsync("auth.ip_denied", "197.50.1.7") >= 1);

            // The user's own list adds a home address and an IPv6 range, without repeating the campus.
            Assert.Equal(403, (await sys.Call("PUT", $"/api/iam/users/{await SysAdminIdAsync()}/ip-allowlist/enabled", Scenario.O(("enabled", true), ("reason", "قيد على حسابي")))).Status);
            await sys.Ok("POST", $"/api/iam/users/{userId}/ip-allowlist/entries", Scenario.O(("value", "197.50.1.7"), ("label", "المنزل"), ("version", null)));
            await sys.Ok("POST", $"/api/iam/users/{userId}/ip-allowlist/entries", Scenario.O(("value", "2001:db8::/64"), ("label", "شبكة IPv6"), ("version", null)));
            var own = await sys.Ok("PUT", $"/api/iam/users/{userId}/ip-allowlist/enabled", Scenario.O(("enabled", true), ("reason", "يعمل من المنزل")));
            Assert.Equal(2, own["entries"]!.AsArray().Count);
            foreach (var (ip, expected) in new[] { ("197.50.1.7", 200), ("2001:db8::42", 200), ("41.65.100.9", 200), ("8.8.8.8", 403) })
            {
                using var browser = new Browser(app.Base, ip);
                Assert.True((await browser.SignInAsync(email, password)).Status == expected, $"{ip}: expected {expected}");
            }

            // Every request of a session, not only the sign-in: the same session from another address is refused, then works again.
            using (var moving = new Browser(app.Base, "41.65.100.9"))
            {
                Assert.Equal(200, (await moving.SignInAsync(email, password)).Status);
                Assert.Equal(200, (await moving.SendAsync(HttpMethod.Get, "/api/me")).Status);
                moving.Ip = "8.8.4.4";
                var (refused, body) = await moving.SendAsync(HttpMethod.Get, "/api/me");
                Assert.Equal((403, "ip_not_allowed"), (refused, body.Code()));
                Assert.Contains("8.8.4.4", body!["title"].Str(), StringComparison.Ordinal);
                moving.Ip = "197.50.1.7";
                Assert.Equal(200, (await moving.SendAsync(HttpMethod.Get, "/api/me")).Status);
            }
            Assert.True(await OutboxAsync("auth.ip_denied", "8.8.4.4") >= 1);

            // With the global list off, the user's own list alone applies to them; others are unrestricted again.
            await sys.Ok("PUT", "/api/security/ip-allowlist/enabled", Scenario.O(("enabled", false), ("reason", "إيقاف قيد الشبكة")));
            using (var campusBrowser = new Browser(app.Base, "41.65.100.9"))
                Assert.Equal((403, "ip_not_allowed"), await campusBrowser.SignInAsync(email, password));
            using (var anywhere = new Browser(app.Base, "8.8.8.8"))
                Assert.Equal(200, (await anywhere.SignInAsync("deptrep@tier1.local", Tier1App.Password)).Status);

            Assert.True(await OutboxAsync("security.ip_global_enable", "global") >= 1);
            Assert.True(await OutboxAsync("security.ip_rule_add", "197.50.1.7/32") >= 1);
            Assert.True(await OutboxAsync("security.ip_user_enable", email) >= 1);
        }
        finally
        {
            // The app is shared by every test class: leave no restriction behind. The last change goes through the API, so this
            // instance drops its cached rules at once (a change made only in SQL would reach it within five seconds).
            await app.ScalarAsync("DELETE FROM iam.ip_allow_rules WHERE user_id IS NULL OR user_id = @u", ("u", Guid.Parse(userId)));
            await app.ScalarAsync("UPDATE iam.users SET ip_restricted = false WHERE id = @u", ("u", Guid.Parse(userId)));
            await sys.Call("PUT", "/api/security/ip-allowlist/enabled", Scenario.O(("enabled", false), ("reason", "انتهى اختبار العناوين")));
        }
    }

    private async Task<string> SysAdminIdAsync() =>
        (string)(await app.ScalarAsync("SELECT id::text FROM iam.users WHERE email = 'sysadmin@tier1.local'"))!;
}
