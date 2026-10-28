using PTenoLauncher.Core;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class FabricVersionConstraintTests
{
    [Theory]
    [InlineData("1.21.1", ">=1.15-alpha.19.39.a", ConstraintMatch.Match)]
    [InlineData("1.15", ">=1.15-alpha.19.39.a", ConstraintMatch.Match)]
    [InlineData("1.15-alpha.19.38.a", ">=1.15-alpha.19.39.a", ConstraintMatch.Mismatch)]
    [InlineData("1.0-beta", ">=1.0", ConstraintMatch.Mismatch)]
    [InlineData("1.0-beta.10", ">1.0-beta.2", ConstraintMatch.Match)]
    [InlineData("1.0-beta.2", ">1.0-beta.10", ConstraintMatch.Mismatch)]
    [InlineData("1.0-alpha", ">1.0-1", ConstraintMatch.Match)]
    [InlineData("1.0-beta.1", ">1.0-beta", ConstraintMatch.Match)]
    [InlineData("1.0-alpha", ">1.0-", ConstraintMatch.Match)]
    [InlineData("1.1-beta", "~1.0", ConstraintMatch.Mismatch)]
    [InlineData("1.0.9+build", "~1.0", ConstraintMatch.Match)]
    [InlineData("1.0-beta", "^0.16", ConstraintMatch.Mismatch)]
    [InlineData("0.99", "^0.16", ConstraintMatch.Match)]
    [InlineData("1.2-alpha", "1.2.x.x", ConstraintMatch.Match)]
    [InlineData("1.2.3+changed", "=1.2.3+original", ConstraintMatch.Match)]
    [InlineData("1.2.3.4.5", ">1.2.3.4.4", ConstraintMatch.Match)]
    [InlineData("1.2", "1.2.0.0", ConstraintMatch.Match)]
    [InlineData("snapshot-a", "=snapshot-a", ConstraintMatch.Match)]
    [InlineData("snapshot-a", "=snapshot-b", ConstraintMatch.Mismatch)]
    [InlineData("snapshot-a", ">=1.0", ConstraintMatch.Unknown)]
    [InlineData("1.0", ">=unsupported-expression", ConstraintMatch.Unknown)]
    [InlineData("1.21.1", ">=1.20 <1.22", ConstraintMatch.Match)]
    [InlineData("1.19", "1.18 || 1.19", ConstraintMatch.Match)]
    public void FollowsFabricPredicateRules(string actual,string expression,ConstraintMatch expected) =>
        Assert.Equal(expected,VersionConstraints.MatchFabric(actual,expression));
}
