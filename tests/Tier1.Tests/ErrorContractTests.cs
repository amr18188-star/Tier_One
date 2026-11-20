using System.Text;

namespace Tier1.Tests;

/// <summary>
/// BE-6: the error contract (docs/api/errors.md). A client reads the status to know what kind of refusal it is, and the code to
/// know which one, so one code always comes with one status: 400 for input the user corrects, 422 for a valid request a rule or
/// the item's state refuses, 409 for a conflict, 403 and 404 for permission and scope.
/// </summary>
public sealed class ErrorContractTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) return dir.FullName;
        throw new InvalidOperationException("Tier1.sln not found above the test output.");
    }

    /// <summary>Every <c>new DomainException("code", ar, en[, status])</c> in the server code, with its status (422 when not given).</summary>
    internal static List<(string Code, int Status, string Where)> Throws()
    {
        var found = new List<(string, int, string)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(file);
            for (var at = text.IndexOf("new DomainException(", StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf("new DomainException(", at + 1, StringComparison.Ordinal))
            {
                var args = Arguments(text, at + "new DomainException(".Length);
                if (args.Count < 3 || !args[0].StartsWith('"')) continue; // a code passed in a variable, or the message-only constructors
                var status = args.Count >= 4 && int.TryParse(args[3].Replace("status:", "", StringComparison.Ordinal).Trim(), out var s) ? s : 422;
                var line = text[..at].Count(c => c == '\n') + 1;
                found.Add((args[0].Trim('"'), status, $"{Path.GetRelativePath(Root, file)}:{line}"));
            }
        }
        return found;
    }

    /// <summary>The top-level arguments of a call starting after its '(' (strings, interpolations and nested calls skipped).</summary>
    private static List<string> Arguments(string text, int start)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                // A string or interpolated string: copy it whole, honouring \" escapes.
                var end = i + 1;
                while (end < text.Length && !(text[end] == '"' && text[end - 1] != '\\')) end++;
                current.Append(text, i, end - i + 1);
                i = end;
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    args.Add(current.ToString().Trim());
                    return args;
                }
                depth--;
            }
            if (c == ',' && depth == 0)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        return args;
    }

    [Fact]
    public void The_scan_finds_the_errors_it_checks() =>
        Assert.True(Throws().Count > 150, "The source scan found too few errors; has DomainException moved?");

    [Fact]
    public void Each_error_code_has_one_status()
    {
        var mixed = Throws().GroupBy(t => t.Code).Where(g => g.Select(t => t.Status).Distinct().Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => $"{t.Status} at {t.Where}"))}").ToList();
        Assert.True(mixed.Count == 0, "Codes thrown with more than one status:\n" + string.Join("\n", mixed));
    }

    [Fact]
    public void Statuses_are_those_of_the_contract()
    {
        int[] allowed = [400, 403, 404, 409, 413, 415, 422, 423, 428, 500];
        var other = Throws().Where(t => !allowed.Contains(t.Status)).Select(t => $"{t.Code} {t.Status} at {t.Where}").ToList();
        Assert.True(other.Count == 0, "Statuses outside docs/api/errors.md:\n" + string.Join("\n", other));
    }
}
