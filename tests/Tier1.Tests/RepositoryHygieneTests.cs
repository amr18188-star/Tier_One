using System.ComponentModel;
using System.Diagnostics;

namespace Tier1.Tests;

/// <summary>
/// D9 (ADR-0009): backups, data exports and build outputs never enter git. Runs with every test run (and in CI), so a pull request
/// that adds one fails before deployment. Without git or outside a clone (a source archive, a container build) there is nothing to check.
/// </summary>
public class RepositoryHygieneTests
{
    private static readonly string[] ForbiddenSuffixes = [".dump", ".bak", ".archive", ".sql.gz", ".bson"];
    private static readonly string[] ForbiddenFolders = ["backups", "bin", "obj", "node_modules"];

    /// <summary>Is this tracked path something that must stay out of git?</summary>
    public static bool Forbidden(string path)
    {
        var p = path.Replace('\\', '/');
        var segments = p.Split('/');
        return segments[..^1].Any(s => ForbiddenFolders.Contains(s, StringComparer.OrdinalIgnoreCase))
               || ForbiddenSuffixes.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase))
               || p.StartsWith("web/.scratch-", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("نقل-المنظومة/backups/tier1.dump", true)]
    [InlineData("نقل-المنظومة/backups/mongo/tier1_logs/audit_events.jsonl", true)]
    [InlineData("data/tier1-2026.bak", true)]
    [InlineData("exports/db.sql.gz", true)]
    [InlineData("web/.scratch-results-scroll-build/index.html", true)]
    [InlineData("src/Tier1.Api/bin/Debug/net10.0/Tier1.Api.dll", true)]
    [InlineData("web/node_modules/react/index.js", true)]
    [InlineData("نقل-المنظومة/restore-here.ps1", false)]
    [InlineData("deploy/docker-compose.yml", false)]
    [InlineData("docs/backups-policy.md", false)]
    [InlineData("src/Tier1.Application/Collection/ResultsService.cs", false)]
    public void Recognises_what_must_stay_out_of_git(string path, bool forbidden) => Assert.Equal(forbidden, Forbidden(path));

    [Fact]
    public void No_backups_or_build_outputs_are_tracked()
    {
        var root = RepoRoot();
        var tracked = root is null ? null : TrackedFiles(root);
        if (tracked is null) return; // no git here: nothing to check
        var bad = tracked.Where(Forbidden).ToList();
        Assert.True(bad.Count == 0,
            $"{bad.Count} backup, export or build file(s) are tracked by git; remove them (git rm --cached) and keep backups in secured storage outside the repository (ADR-0009):\n"
            + string.Join("\n", bad.Take(20)));
    }

    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) return dir.FullName;
        return null;
    }

    private static List<string>? TrackedFiles(string root)
    {
        try
        {
            var start = new ProcessStartInfo("git", "-c core.quotepath=off ls-files -z")
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var git = Process.Start(start);
            if (git is null) return null;
            var output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            return git.ExitCode == 0 ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)] : null;
        }
        catch (Win32Exception)
        {
            return null; // git is not installed
        }
    }
}
