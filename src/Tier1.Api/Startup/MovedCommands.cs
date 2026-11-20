namespace Tier1.Api.Startup;

/// <summary>
/// ARCH-4: the one-off commands that left the server for tools/Tier1.Tools. Given to the API by habit (an old runbook, a cron
/// line), they would otherwise start a second server; the API names the tool instead and exits.
/// </summary>
public static class MovedCommands
{
    private static readonly string[] Names =
    [
        "--data-fixes", "--apply-data-fixes", "--mongo-setup", "--reset-kpi", "--load-structure",
        "--backup", "--restore-test", "--import-structure", "--import-kpi", "--demo-data",
    ];

    /// <summary>The message for a moved command among the arguments, or null.</summary>
    public static string? Refusal(string[] args) =>
        args.FirstOrDefault(a => Names.Contains(a, StringComparer.Ordinal)) is { } moved
            ? $"{moved} is no longer a server command: run it with Tier1.Tools (dotnet run --project tools/Tier1.Tools -- {moved} ..., " +
              "or the tools image in deploy/docker-compose.yml). The server keeps only --migrate and --create-superadmin."
            : null;
}
