using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace Tier1.IntegrationTests;

/// <summary>
/// The whole application in the test process (<see cref="WebApplicationFactory{TEntryPoint}"/> on real Kestrel),
/// against throwaway PostgreSQL 18 and MongoDB 8 containers: migrations, catalog roles, demo accounts and demo colleges, nothing else.
/// Needs Docker. One instance serves every test class in the "app" collection; each test makes its own trial cycle, so tests do not share forms.
/// </summary>
public sealed class Tier1App : IAsyncLifetime
{
    public const string Password = "Tier1-Integration-Pass1";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("tier1").WithUsername("tier1_dev").WithPassword("tier1_dev")
        .WithEnvironment("TZ", "Africa/Cairo").WithEnvironment("PGTZ", "Africa/Cairo").Build();

    private readonly MongoDbContainer mongo = new MongoDbBuilder("mongo:8").Build();
    private readonly Dictionary<string, DemoClient> clients = [];
    private readonly string dataDir = Path.Combine(Path.GetTempPath(), "tier1-it-" + Guid.NewGuid().ToString("N")[..8]);
    private Factory? factory;

    public Uri Base { get; private set; } = null!;

    public string ConnectionString => postgres.GetConnectionString();

    /// <summary>The application's services, for checks that call a service directly in a scope of its own.</summary>
    public IServiceProvider Services => factory!.Services;

    /// <summary>Where the app stores uploaded files (Files:Root) in this run.</summary>
    public string FilesRoot => Path.Combine(dataDir, "files");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), mongo.StartAsync());
        // Environment variables override appsettings.Development.json and user secrets, and Program reads them as it starts,
        // so nothing here can reach a developer's own databases.
        var environment = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ConnectionStrings__Tier1"] = postgres.GetConnectionString(),
            ["ConnectionStrings__Mongo"] = mongo.GetConnectionString(),
            ["ConnectionStrings__MongoAdmin"] = mongo.GetConnectionString(),
            ["Seed__Demo"] = "true",
            ["Seed__DemoData"] = "false",
            ["Seed__Structure"] = "false",
            ["Seed__AutoDataFixes"] = "true",
            ["Seed__DemoPassword"] = Password,
            ["Files__Root"] = Path.Combine(dataDir, "files"),
            ["DataProtection__KeysPath"] = Path.Combine(dataDir, "keys"),
            ["Email__PickupDirectory"] = Path.Combine(dataDir, "mail"),
            ["MongoLogs__CheckpointDirectory"] = Path.Combine(dataDir, "checkpoints"),
            ["App__PublicUrl"] = "http://127.0.0.1",
        };
        foreach (var (key, value) in environment) Environment.SetEnvironmentVariable(key, value);
        factory = new Factory();
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Base = new Uri(address.Replace("[::]", "127.0.0.1", StringComparison.Ordinal).Replace("localhost", "127.0.0.1", StringComparison.Ordinal));
    }

    public async Task DisposeAsync()
    {
        foreach (var c in clients.Values) c.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), mongo.DisposeAsync().AsTask());
        try { Directory.Delete(dataDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A signed-in demo account (<c>name@tier1.local</c>), kept for the run like a browser session.</summary>
    internal async Task<DemoClient> As(string name)
    {
        if (!clients.TryGetValue(name, out var client))
            clients[name] = client = await DemoClient.LoginAsync(Base, $"{name}@tier1.local", Password, CancellationToken.None);
        return client;
    }

    /// <summary>A scalar from the application database, for checks the API does not expose (outbox rows, events).</summary>
    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100 // The SQL is a constant in the test; values go in as parameters.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A scalar from SQL with parameters (a timestamp, a count), or the rows changed by an UPDATE.</summary>
    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100 // The SQL is a constant in the test; values go in as parameters.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ? await command.ExecuteNonQueryAsync() : await command.ExecuteScalarAsync();
    }

    /// <summary>A new session of a demo account, not shared with the other tests (for session timing checks).</summary>
    internal Task<DemoClient> NewSessionAsync(string name) => DemoClient.LoginAsync(Base, $"{name}@tier1.local", Password, CancellationToken.None);

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }
}

[CollectionDefinition("app")]
public sealed class AppCollection : ICollectionFixture<Tier1App>;
