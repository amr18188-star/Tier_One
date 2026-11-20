namespace Tier1.IntegrationTests;

/// <summary>FE-2: background polls (X-Passive: 1) read the session without sliding its idle window; real requests slide it.</summary>
[Collection("app")]
public sealed class SessionTests(Tier1App app)
{
    private const string Latest = "(SELECT s.id FROM iam.sessions s JOIN iam.users u ON u.id = s.user_id WHERE u.email = @email ORDER BY s.created_at DESC LIMIT 1)";

    private static async Task<int> MeAsync(DemoClient client, bool passive)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        if (passive) request.Headers.Add("X-Passive", "1");
        using var response = await client.SendAsync(request, CancellationToken.None);
        return (int)response.StatusCode;
    }

    private Task<object?> AgeSessionAsync(string email, int minutes) =>
        app.ScalarAsync($"UPDATE iam.sessions SET last_seen_at = now() - make_interval(mins => @m) WHERE id = {Latest}", ("email", email), ("m", minutes));

    private async Task<double> IdleMinutesAsync(string email) =>
        Convert.ToDouble(await app.ScalarAsync($"SELECT extract(epoch FROM now() - last_seen_at) / 60 FROM iam.sessions WHERE id = {Latest}", ("email", email)),
            System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_background_poll_does_not_keep_the_session_alive()
    {
        const string email = "viewer@tier1.local";
        using var client = await app.NewSessionAsync("viewer");
        await AgeSessionAsync(email, 10);

        Assert.Equal(200, await MeAsync(client, passive: true));
        Assert.True(await IdleMinutesAsync(email) > 9, "The poll did not slide the idle window");

        Assert.Equal(200, await MeAsync(client, passive: false));
        Assert.True(await IdleMinutesAsync(email) < 1, "A real request slides it");
    }

    [Fact]
    public async Task An_idle_session_ends_even_while_polled()
    {
        using var client = await app.NewSessionAsync("viewer");
        await AgeSessionAsync("viewer@tier1.local", 31);
        Assert.Equal(401, await MeAsync(client, passive: true));
    }
}
