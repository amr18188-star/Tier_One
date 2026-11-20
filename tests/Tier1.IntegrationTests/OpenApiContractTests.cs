using System.Text.Json;

namespace Tier1.IntegrationTests;

/// <summary>
/// BE-22: the API contract is pinned in docs/api/openapi.json. A change to a route, a parameter or a request shape shows here and
/// in review instead of only in the browser. To accept a deliberate change, run this test with T1_UPDATE_OPENAPI=1 and commit the file.
/// </summary>
[Collection("app")]
public sealed class OpenApiContractTests(Tier1App app)
{
    [Fact]
    public async Task The_api_matches_the_committed_contract()
    {
        var planner = await app.As("planner");
        var document = (await planner.Get("/openapi/v1.json"))!.AsObject();
        // The server address is the test's random port, not part of the contract.
        document.Remove("servers");
        var actual = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(RepoRoot(), "docs", "api", "openapi.json");
        if (Environment.GetEnvironmentVariable("T1_UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"{path} is missing; run this test with T1_UPDATE_OPENAPI=1 and commit it.");
        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            var (e, a) = (expected.Split('\n'), actual.Split('\n'));
            var line = Enumerable.Range(0, Math.Min(e.Length, a.Length)).FirstOrDefault(i => e[i] != a[i], Math.Min(e.Length, a.Length));
            Assert.Fail($"The API differs from docs/api/openapi.json at line {line + 1}:\n  committed: {e.ElementAtOrDefault(line)}\n  now:       {a.ElementAtOrDefault(line)}\n" +
                "If the change is deliberate, run this test with T1_UPDATE_OPENAPI=1 and commit the file with the change.");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tier1.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tier1.sln not found above the test folder.");
    }
}
