using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tier1.Api.Startup;

namespace Tier1.Tests;

/// <summary>
/// D10 (ADR-0010): no automatic seeding in production; base data and data fixes go through approved, recorded commands;
/// nothing in production data is replaced or deleted by a seed command.
/// </summary>
public class ProductionSeedTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tier1.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Theory]
    [InlineData("Database:MigrateOnStartup")]
    [InlineData("Seed:Structure")]
    [InlineData("Seed:AutoDataFixes")]
    public void Automatic_seeding_is_refused_outside_development(string key)
    {
        foreach (var env in new[] { "Production", "Staging" })
        {
            var e = Assert.Throws<InvalidOperationException>(() => StartupGuards.Check(Config((key, "true")), new Env(env)));
            Assert.Contains(key, e.Message);
        }
    }

    [Fact]
    public void Development_keeps_automatic_seeding()
    {
        // Development checks only the loopback binding, so the dev, flow and E2E environments stay as they are.
        StartupGuards.Check(Config(("Database:MigrateOnStartup", "true"), ("Seed:Structure", "true"), ("Seed:AutoDataFixes", "true")), new Env(Environments.Development));
    }

    [Fact]
    public void Resetting_indicator_data_is_refused_in_production_only()
    {
        Assert.Contains("refused in Production", SeedCommands.Refusal("--reset-kpi", production: true, existingUnits: 0));
        Assert.Null(SeedCommands.Refusal("--reset-kpi", production: false, existingUnits: 120));
    }

    [Fact]
    public void The_official_structure_loads_in_production_only_on_a_database_without_units()
    {
        Assert.Null(SeedCommands.Refusal("--load-structure", production: true, existingUnits: 0));
        Assert.Contains("first setup", SeedCommands.Refusal("--load-structure", production: true, existingUnits: 246));
        Assert.Null(SeedCommands.Refusal("--load-structure", production: false, existingUnits: 246));
    }

    [Fact]
    public void Production_commands_name_the_approving_administrator()
    {
        Assert.Throws<ArgumentException>(() => SeedCommands.Operator(["--apply-data-fixes", "--confirm"], production: true, "--apply-data-fixes"));
        Assert.Equal("أ. محمد (--apply-data-fixes)", SeedCommands.Operator(["--apply-data-fixes", "--confirm", "--operator", "أ. محمد"], production: true, "--apply-data-fixes"));
        // Outside production the operating-system user stands in.
        Assert.EndsWith("(--load-structure)", SeedCommands.Operator(["--load-structure", "--confirm"], production: false, "--load-structure"));
    }
}

/// <summary>A job or command without a signed-in user is recorded under the name it gives (the approving operator), else "النظام".</summary>
public class AuditActorNameTests
{
    [Fact]
    public void A_command_records_the_operator_it_names()
    {
        Assert.Equal("مدير الاختبار (--apply-data-fixes)", Tier1.Application.Auditing.AuditLog.ActorNameFor(null, "job", null, "مدير الاختبار (--apply-data-fixes)"));
        Assert.Equal("النظام", Tier1.Application.Auditing.AuditLog.ActorNameFor(null, "job", null, null));
        Assert.Equal("سارة", Tier1.Application.Auditing.AuditLog.ActorNameFor(Guid.NewGuid(), "web", "سارة", "ignored"));
        Assert.Equal("أحمد", Tier1.Application.Auditing.AuditLog.ActorNameFor(null, "web", null, "أحمد")); // sign-in before the session exists
    }
}
