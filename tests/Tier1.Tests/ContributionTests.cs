using Tier1.Application.Collection;

namespace Tier1.Tests;

/// <summary>A college's share of the university target in a sum (count) indicator: its target, and what it achieved.</summary>
public class ContributionTests
{
    [Theory]
    [InlineData(3, 60, 0.05)]      // the college target is 5% of the university target
    [InlineData(2, 60, 0.0333)]    // and what it achieved is 3.3% of it
    [InlineData(75, 60, 1.25)]     // a college can exceed the whole university target
    public void Share_of_the_university_target(double part, double universityTarget, double expected) =>
        Assert.Equal((decimal)expected, ResultsRules.ShareOfUniversity((decimal)part, (decimal)universityTarget));

    [Fact]
    public void No_share_without_a_part_or_a_university_target_or_with_a_zero_target()
    {
        Assert.Null(ResultsRules.ShareOfUniversity(null, 60));
        Assert.Null(ResultsRules.ShareOfUniversity(3, null));
        Assert.Null(ResultsRules.ShareOfUniversity(3, 0));
    }
}
