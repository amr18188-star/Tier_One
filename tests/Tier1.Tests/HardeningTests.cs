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
}

/// <summary>حجب التخمين لكل (حساب، عنوان): لا يقفل أحد حساب غيره من مكان واحد.</summary>
public class LoginThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Block = TimeSpan.FromMinutes(15);

    [Fact]
    public void An_address_is_blocked_for_one_account_only_after_the_limit()
    {
        var t = new Tier1.Application.Security.LoginThrottle(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var account = Guid.NewGuid();
        for (var i = 0; i < 4; i++) Assert.False(t.Fail("staff", account, "10.0.0.1", Now, 5, Block));
        Assert.True(t.Fail("staff", account, "10.0.0.1", Now, 5, Block));
        Assert.True(t.IsBlocked("staff", account, "10.0.0.1", Now));
        Assert.False(t.IsBlocked("staff", account, "10.0.0.2", Now));
        Assert.False(t.IsBlocked("portal", account, "10.0.0.1", Now));
        Assert.False(t.IsBlocked("staff", Guid.NewGuid(), "10.0.0.1", Now));
        Assert.False(t.IsBlocked("staff", account, "10.0.0.1", Now + Block + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_successful_sign_in_clears_the_count()
    {
        var t = new Tier1.Application.Security.LoginThrottle(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var account = Guid.NewGuid();
        for (var i = 0; i < 4; i++) t.Fail("portal", account, "10.0.0.1", Now, 5, Block);
        t.Reset("portal", account, "10.0.0.1");
        Assert.False(t.Fail("portal", account, "10.0.0.1", Now, 5, Block));
        Assert.False(t.IsBlocked("portal", account, "10.0.0.1", Now));
    }
}
