using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Tier1.Tests;

/// <summary>
/// BE-4: ExecuteUpdate and ExecuteDelete commit at once, outside SaveChanges. A service method that uses one of them and also
/// saves tracked changes (the main write and its audit row) must run both in one explicit transaction, or a failure in the
/// second step leaves the first applied and unaudited. A method that means it says so with a "tx-exempt:" comment and its reason.
/// </summary>
public partial class TransactionRulesTests
{
    [GeneratedRegex(@"^    (?:public|private|internal|protected)[^\n=;]*\basync\b[^\n;]*\(", RegexOptions.Multiline)]
    private static partial Regex MethodStart();

    private static string ApplicationDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Tier1.Application"));

    [Fact]
    public void Bulk_statements_and_tracked_saves_share_one_transaction()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ApplicationDir(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            var starts = MethodStart().Matches(text).Select(m => m.Index).Append(text.Length).ToList();
            for (var i = 0; i < starts.Count - 1; i++)
            {
                var body = text[starts[i]..starts[i + 1]];
                var bulk = body.Contains("ExecuteUpdateAsync(", StringComparison.Ordinal) || body.Contains("ExecuteDeleteAsync(", StringComparison.Ordinal);
                var saves = body.Contains("SaveChangesAsync(", StringComparison.Ordinal);
                // A lock port (ITransactionLock, IIamLock) opens the transaction itself: locks.BeginAsync(...), iamLock.BeginAsync(...).
                var transaction = body.Contains("BeginTransactionAsync(", StringComparison.Ordinal) || body.Contains("BeginLockedAsync(", StringComparison.Ordinal)
                                  || body.Contains("locks.BeginAsync(", StringComparison.Ordinal) || body.Contains("iamLock.BeginAsync(", StringComparison.Ordinal);
                // A method may opt out with a "tx-exempt:" comment that says why (sign-in attempt counters must commit on their own).
                var exempt = body.Contains("tx-exempt:", StringComparison.Ordinal);
                if (bulk && saves && !transaction && !exempt)
                    offenders.Add($"{Path.GetFileName(file)}: {body[..body.IndexOf('(', StringComparison.Ordinal)].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "Bulk statement and SaveChanges without a transaction:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_rule_sees_the_application_sources()
    {
        Assert.True(Directory.EnumerateFiles(ApplicationDir(), "*Service.cs", SearchOption.AllDirectories).Count() > 10);
    }
}
