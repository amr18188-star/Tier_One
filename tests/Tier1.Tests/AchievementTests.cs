using System.Text.Json;
using Tier1.Application.Collection;
using Tier1.Application.Kpi;
using Tier1.Application.Scorecard;
using Tier1.Domain.Kpi;

namespace Tier1.Tests;

/// <summary>
/// D7 (ADR-0006): one achievement formula for results and the scorecard. The results judge the exact ratio; the scorecard shows it
/// the framework way (capped at 100, one decimal). They must never drift apart again.
/// </summary>
public class AchievementTests
{
    [Theory]
    // Higher is better.
    [InlineData(Direction.HigherIsBetter, 90, 100, 0.9)]
    [InlineData(Direction.HigherIsBetter, 120, 100, 1.2)]
    [InlineData(Direction.HigherIsBetter, 0, 0, 1)]      // 2.02 enrolment growth: target zero, zero or more is achieved
    [InlineData(Direction.HigherIsBetter, -3, 0, 0)]
    // Lower is better.
    [InlineData(Direction.LowerIsBetter, 40, 30, 0.75)]
    [InlineData(Direction.LowerIsBetter, 0, 30, 1)]
    [InlineData(Direction.LowerIsBetter, 3, 0, 0.97)]    // framework workbook: (100 - value) / 100 with a zero target
    [InlineData(Direction.LowerIsBetter, 0, 0, 1)]
    // Reach a value (calculation-review.md): 1 - deviation ratio, never below zero.
    [InlineData(Direction.Range, 13, 12, 0.9166666666666666666666666667)]
    [InlineData(Direction.Range, 11, 12, 0.9166666666666666666666666667)]
    [InlineData(Direction.Range, 30, 12, 0)]
    [InlineData(Direction.Range, 0, 0, 1)]
    public void The_single_formula_by_direction(Direction direction, double actual, double target, double expected) =>
        Assert.Equal((decimal)expected, Achievement.Ratio(direction, (decimal)actual, (decimal)target)!.Value, 6);

    [Fact]
    public void Nothing_to_judge_without_a_value_a_target_or_a_direction()
    {
        Assert.Null(Achievement.Ratio(Direction.HigherIsBetter, null, 10));
        Assert.Null(Achievement.Ratio(Direction.HigherIsBetter, 10, null));
        Assert.Null(Achievement.Ratio(Direction.Undefined, 10, 10));
    }

    [Fact]
    public void The_scorecard_now_understands_reach_a_value()
    {
        // Before D7 the scorecard treated "reach a value" as higher is better: 13 against 12 was 100%.
        Assert.Equal(91.7m, ScorecardRules.Achievement(13, 12, Direction.Range));
        Assert.Equal(0.9167m, ResultsRules.Achievement(Direction.Range, 13, 12));
    }

    /// <summary>For every indicator of the official framework, every yearly target and a spread of values, both screens agree.</summary>
    [Fact]
    public void Results_and_scorecard_agree_on_all_framework_indicators()
    {
        using var stream = typeof(Tier1.Infrastructure.DependencyInjection).Assembly
            .GetManifestResourceStream("Tier1.Infrastructure.Persistence.Seed.kpi-framework.json");
        var seed = JsonDocument.Parse(stream!).RootElement;
        var checks = 0;
        foreach (var indicator in seed.GetProperty("indicators").EnumerateArray())
        {
            var direction = Enum.Parse<Direction>(indicator.GetProperty("direction").GetString()!);
            foreach (var t in indicator.GetProperty("targets").EnumerateArray())
            {
                if (t.GetProperty("value").ValueKind != JsonValueKind.Number) continue;
                var target = t.GetProperty("value").GetDecimal();
                foreach (var factor in new[] { 0m, 0.37m, 0.5m, 0.9m, 1m, 1.3m, 2m })
                {
                    var value = target == 0 ? factor * 10 : target * factor;
                    var results = ResultsRules.Achievement(direction, value, target);
                    var scorecard = ScorecardRules.Achievement(value, target, direction);
                    Assert.Equal(results is null, scorecard is null);
                    if (results is { } r)
                        Assert.True(Math.Abs(Math.Min(r * 100, 100) - scorecard!.Value) <= 0.05m,
                            $"{indicator.GetProperty("code").GetString()} {direction} value {value} target {target}: results {r * 100}, scorecard {scorecard}");
                    checks++;
                }
            }
        }
        Assert.True(checks >= 92 * 5 * 7 - 100, $"only {checks} checks");
    }
}
