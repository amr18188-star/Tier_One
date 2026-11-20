using Tier1.Application.Common;
using Tier1.Api.Startup;
using Tier1.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Tier1.Tests;

/// <summary>إصلاحات مراجعة المتانة والأمن: حدود النصوص، وشروط البدء، والحد من تخمين كلمات المرور.</summary>
public class HardeningTests
{
    [Fact]
    public void Texts_have_a_maximum_that_matches_the_columns()
    {
        Assert.Equal("سبب كاف", Require.Reason("  سبب كاف  "));
        Assert.Equal("text_too_long", Assert.Throws<DomainException>(() => Require.Reason(new string('س', 501))).Code);
        Assert.Equal("text_too_long", Assert.Throws<DomainException>(() => Require.Optional(new string('x', 101), max: 100)).Code);
        Assert.Null(Require.Optional("   "));
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tier1";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Theory]
    [InlineData("http://localhost:5150")]
    [InlineData("http://127.0.0.1:5150;https://[::1]:5151")]
    public void Development_on_loopback_starts(string urls) =>
        StartupGuards.Check(Config(("urls", urls)), new Env(Environments.Development));

    [Theory]
    [InlineData("http://0.0.0.0:5150")]
    [InlineData("http://*:5150")]
    [InlineData("http://10.1.2.3:5150")]
    public void Development_on_the_network_refuses_to_start(string urls) =>
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Check(Config(("urls", urls)), new Env(Environments.Development)));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Prod")]
    public void Every_non_development_environment_rejects_demo_settings(string env) =>
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Check(Config(("Seed:Demo", "true")), new Env(env)));

    /// <summary>BE-9: mail links must lead to the address users open, never to a silent localhost default.</summary>
    [Theory]
    [InlineData("https://tier1.university.edu.eg", true)]
    [InlineData("https://tier1.university.edu.eg/", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("http://tier1.university.edu.eg", false)]
    [InlineData("https://localhost:5150", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("tier1.university.edu.eg", false)]
    public void Outside_development_the_public_url_is_an_https_address_of_the_server(string? value, bool ok) =>
        Assert.Equal(ok, StartupGuards.PublicUrlOk(value));
}

// The sign-in counters moved to PostgreSQL, shared by every API instance: their tests are in
// tests/Tier1.IntegrationTests/LoginAttemptTests.cs, against a real database and a second instance.
