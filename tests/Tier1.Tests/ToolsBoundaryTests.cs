using System.Xml.Linq;

namespace Tier1.Tests;

/// <summary>
/// ARCH-4: maintenance, import and demo-data tooling lives in tools/Tier1.Tools, not in the production API, and the E2E suite
/// reaches the server through HTTP only (tests/Tier1.TestSupport), without referencing any server project.
/// </summary>
public sealed class ToolsBoundaryTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) return dir.FullName;
        throw new InvalidOperationException("Tier1.sln not found above the test output.");
    }

    [Fact]
    public void The_api_carries_no_maintenance_import_or_demo_code()
    {
        var api = typeof(Tier1.Api.Startup.ApiSetup).Assembly;
        var moved = new[] { "Maintenance", "SeedCommands", "SeedImporters", "DemoDataSeeder", "DemoRun", "DemoClient" };
        var found = api.GetTypes().Concat(typeof(Tier1.Infrastructure.DependencyInjection).Assembly.GetTypes())
            .Where(t => moved.Contains(t.Name)).Select(t => t.FullName).ToList();
        Assert.True(found.Count == 0, $"Tooling found in the server assemblies: {string.Join(", ", found)}; it belongs in tools/Tier1.Tools.");
    }

    [Fact]
    public void The_api_answers_only_the_migrate_and_create_superadmin_commands()
    {
        var program = File.ReadAllText(Path.Combine(Root, "src", "Tier1.Api", "Program.cs"));
        var commands = System.Text.RegularExpressions.Regex.Matches(program, "\"(--[a-z-]+)\"").Select(m => m.Groups[1].Value).Distinct().Order().ToList();
        Assert.Equal(["--create-superadmin", "--migrate"], commands);
    }

    [Theory]
    [InlineData("--backup")]
    [InlineData("--load-structure")]
    [InlineData("--apply-data-fixes")]
    [InlineData("--demo-data")]
    public void A_moved_command_given_to_the_api_names_the_tool_instead_of_starting_a_server(string command) =>
        Assert.Contains("Tier1.Tools", Tier1.Api.Startup.MovedCommands.Refusal([command, "x"]));

    [Fact]
    public void The_server_commands_and_settings_are_not_refused() =>
        Assert.Null(Tier1.Api.Startup.MovedCommands.Refusal(["--migrate", "--urls", "http://localhost:5150"]));

    [Fact]
    public void The_e2e_suite_references_no_server_project()
    {
        var project = XDocument.Load(Path.Combine(Root, "tests", "Tier1.E2E", "Tier1.E2E.csproj"));
        var references = project.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include") ?? "").ToList();
        Assert.All(references, r => Assert.DoesNotContain(@"\src\", r, StringComparison.Ordinal));
        Assert.Contains(references, r => r.EndsWith("Tier1.TestSupport.csproj", StringComparison.Ordinal));
    }
}
