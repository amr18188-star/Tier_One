using Microsoft.Extensions.Configuration;
using Npgsql;
using Tier1.Infrastructure;

namespace Tier1.Tests;

/// <summary>
/// The PostgreSQL pool has an explicit maximum below the server's connection limit, so one instance under load cannot take
/// every connection (a second instance, the tools and an administrator still connect).
/// </summary>
public sealed class PoolLimitsTests
{
    private const string Plain = "Host=db;Database=tier1;Username=u;Password=p";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Without_a_setting_the_pool_takes_the_default_and_returns_idle_connections_within_a_minute()
    {
        var built = new NpgsqlConnectionStringBuilder(PoolLimits.Apply(Plain, Config()));
        Assert.Equal(PoolLimits.DefaultMaxPoolSize, built.MaxPoolSize);
        Assert.True(built.MaxPoolSize < 100, "The default must stay below PostgreSQL's default max_connections (100).");
        Assert.Equal(60, built.ConnectionIdleLifetime);
    }

    [Fact]
    public void Database_MaxPoolSize_sets_it() =>
        Assert.Equal(45, new NpgsqlConnectionStringBuilder(PoolLimits.Apply(Plain, Config(("Database:MaxPoolSize", "45")))).MaxPoolSize);

    [Theory]
    [InlineData("Maximum Pool Size=12")]
    [InlineData("MaxPoolSize=12")]
    public void A_maximum_in_the_connection_string_wins(string setting) =>
        Assert.Equal(12, new NpgsqlConnectionStringBuilder(PoolLimits.Apply($"{Plain};{setting}", Config(("Database:MaxPoolSize", "45")))).MaxPoolSize);
}
