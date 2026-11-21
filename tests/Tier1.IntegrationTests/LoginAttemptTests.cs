using System.Net.Http.Json;

namespace Tier1.IntegrationTests;

/// <summary>
/// Sign-in counters shared by every API instance (PostgreSQL): per account at one address, per address across accounts, and
/// per account; every attempt recorded. Addresses are simulated with X-Forwarded-For (the test host trusts 127.0.0.1 as a proxy).
/// </summary>
[Collection("app")]
public sealed class LoginAttemptTests(Tier1App app)
{
    private const string Wrong = "Not-The-Password-1";

    /// <summary>One sign-in from an address, as a browser does: a CSRF token, then the form. Returns the status and error code.</summary>
    private static async Task<(int Status, string? Code)> SignInAsync(Uri server, string ip, string email, string password)
    {
        using var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer(), UseCookies = true, CheckCertificateRevocationList = true };
        using var http = new HttpClient(handler) { BaseAddress = server };
        http.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        var csrf = await http.GetFromJsonAsync<JsonObject>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-XSRF-TOKEN", csrf!["token"]!.GetValue<string>());
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var body = text.Length > 0 ? JsonNode.Parse(text) : null;
        return ((int)response.StatusCode, body?["code"]?.GetValue<string>());
    }

    private static string Address() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private Task<long> AttemptsAsync(string email, string outcome) =>
        app.CountAsync("SELECT count(*) FROM iam.login_attempts a JOIN iam.users u ON u.id = a.user_id WHERE u.email = @e AND a.outcome = @o",
            ("e", email), ("o", outcome));

    [Fact]
    public async Task An_address_that_guesses_one_account_is_blocked_for_it_while_the_owner_signs_in_elsewhere()
    {
        const string email = "deptrep@tier1.local";
        var (guesser, owner) = (Address(), Address());
        for (var i = 0; i < 5; i++)
            Assert.Equal((401, "invalid_credentials"), await SignInAsync(app.Base, guesser, email, Wrong));
        // Even the right password from that address is refused now, with the same answer as a wrong one.
        Assert.Equal((401, "invalid_credentials"), await SignInAsync(app.Base, guesser, email, Tier1App.Password));
        Assert.Equal(200, (await SignInAsync(app.Base, owner, email, Tier1App.Password)).Status);

        Assert.True(await AttemptsAsync(email, "failed") >= 5);
        Assert.True(await AttemptsAsync(email, "blocked") >= 1);
        Assert.True(await AttemptsAsync(email, "succeeded") >= 1);
    }

    [Fact]
    public async Task Switching_api_instances_does_not_reset_a_count()
    {
        const string email = "rep.ele@tier1.local";
        var ip = Address();
        var (second, instance) = Tier1App.StartSecondInstance();
        await using (instance)
        {
            // Five failures spread over two instances block the address for this account on both.
            for (var i = 0; i < 5; i++)
                Assert.Equal(401, (await SignInAsync(i % 2 == 0 ? app.Base : second, ip, email, Wrong)).Status);
            Assert.Equal(401, (await SignInAsync(second, ip, email, Tier1App.Password)).Status);
            Assert.Equal(401, (await SignInAsync(app.Base, ip, email, Tier1App.Password)).Status);
        }
        Assert.Equal(1, await app.CountAsync(
            "SELECT count(*) FROM iam.login_counters WHERE key LIKE '%|acct|%|' || @ip AND failures = 5 AND blocked_until > now()", ("ip", ip)));
        Assert.Equal(200, (await SignInAsync(app.Base, Address(), email, Tier1App.Password)).Status);
    }

    [Fact]
    public async Task An_address_spraying_many_accounts_is_blocked_but_its_own_users_still_sign_in()
    {
        var ip = Address();
        // rep.mec signs in from this address first: it is a known place for that account.
        Assert.Equal(200, (await SignInAsync(app.Base, ip, "rep.mec@tier1.local", Tier1App.Password)).Status);
        // Twelve failures on other accounts and unknown emails (the test host's address limit) block the address.
        for (var i = 0; i < 12; i++)
        {
            var target = i % 3 == 0 ? "rep.ai@tier1.local" : $"nobody{i}@tier1.local";
            Assert.Equal(401, (await SignInAsync(app.Base, ip, target, Wrong)).Status);
        }
        // Another account with its right password is refused from that address; from elsewhere it signs in.
        Assert.Equal((401, "invalid_credentials"), await SignInAsync(app.Base, ip, "rep.se@tier1.local", Tier1App.Password));
        Assert.Equal(200, (await SignInAsync(app.Base, Address(), "rep.se@tier1.local", Tier1App.Password)).Status);
        // The account that signed in from this address before is not locked out by someone else's guessing there.
        Assert.Equal(200, (await SignInAsync(app.Base, ip, "rep.mec@tier1.local", Tier1App.Password)).Status);
        Assert.Equal(1, await app.CountAsync(
            "SELECT count(*) FROM iam.login_attempts WHERE ip = @ip AND outcome = 'blocked' AND reason = 'address_blocked'", ("ip", ip)));
    }

    [Fact]
    public async Task Parallel_guesses_cannot_exceed_the_limit()
    {
        const string email = "rep.ai@tier1.local";
        var ip = Address();
        // The address-spraying test also fails on this account without signing in afterwards, so only this test's own failures count.
        var before = await app.CountAsync("SELECT failed_logins FROM iam.users WHERE email = @e", ("e", email));
        // Twice the account-wide limit (20), sent together from one address.
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => SignInAsync(app.Base, ip, email, Wrong)));
        Assert.All(results, r => Assert.Equal(401, r.Status));
        // The atomic count stops at the limit and sets the block once, however many arrive together.
        Assert.Equal(5, await app.CountAsync("SELECT failures FROM iam.login_counters WHERE key LIKE '%|acct|%|' || @ip", ("ip", ip)));
        // The attempt is reserved before the password check, so only those five reach the account's own count: guesses sent
        // together from one address can no longer lock the account for its owner everywhere.
        Assert.True(await app.CountAsync("SELECT failed_logins FROM iam.users WHERE email = @e", ("e", email)) - before <= 5);
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM iam.users WHERE email = @e AND locked_until > now()", ("e", email)));
        Assert.Equal(200, (await SignInAsync(app.Base, Address(), email, Tier1App.Password)).Status);
    }

    [Fact]
    public async Task The_attempt_log_keeps_no_typed_email()
    {
        var ip = Address();
        const string typed = "someone.unknown@tier1.local";
        Assert.Equal(401, (await SignInAsync(app.Base, ip, typed, Wrong)).Status);
        Assert.Equal(1, await app.CountAsync(
            "SELECT count(*) FROM iam.login_attempts WHERE ip = @ip AND user_id IS NULL AND reason = 'unknown_email' AND length(email_hash) = 64", ("ip", ip)));
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM iam.login_attempts WHERE email_hash LIKE '%@%'"));
    }
}
