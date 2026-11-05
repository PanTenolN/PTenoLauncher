using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class MavenQualifierTests
{
    // Authoritative order/aliases: https://maven.apache.org/pom.html#version-order-specification
    // This fixture set covers our documented single-hyphen subset, not arbitrary ComparableVersion inputs.
    [Theory]
    [InlineData("1.0-alpha1", "[1.0-alpha,1.0-beta)", ConstraintMatch.Match)]
    [InlineData("1.0-a1", "[1.0-alpha1]", ConstraintMatch.Match)]
    [InlineData("1.0-B2", "[1.0-beta2]", ConstraintMatch.Match)]
    [InlineData("1.0-m3", "[1.0-milestone3]", ConstraintMatch.Match)]
    [InlineData("1.0-cr2", "[1.0-rc2]", ConstraintMatch.Match)]
    [InlineData("1.0-RC2", "[1.0-rc1,1.0)", ConstraintMatch.Match)]
    [InlineData("1.0-RC2", "[1.0,)", ConstraintMatch.Mismatch)]
    [InlineData("1.0-SNAPSHOT", "(1.0-rc9,1.0)", ConstraintMatch.Match)]
    [InlineData("1.0-final", "[1.0]", ConstraintMatch.Match)]
    [InlineData("1.0-ga", "[1.0.0]", ConstraintMatch.Match)]
    [InlineData("1.0-release", "[1.0]", ConstraintMatch.Match)]
    [InlineData("1.0-sp2", "(1.0,1.0.1)", ConstraintMatch.Match)]
    [InlineData("1.0.1-alpha1", "(1.0-sp99,)", ConstraintMatch.Match)]
    [InlineData("1.0-beta11", "(1.0-beta2,1.0-beta12)", ConstraintMatch.Match)]
    [InlineData("1.0-alpha", "(,1.0-alpha),[1.0-beta,)", ConstraintMatch.Mismatch)]
    [InlineData("1.0-beta2", "[1.0-rc1,1.0-beta1]", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1.0-alpha,1.0-rc2],[1.0-beta,)", ConstraintMatch.Unknown)]
    [InlineData("arbitrary", "1.0-beta1", ConstraintMatch.Match)] // bare Maven recommendation is not a restriction
    public void KnownQualifierRangesHaveDefinedOutcomes(string actual, string expression, ConstraintMatch expected)
        => Assert.Equal(expected, MavenVersionConstraints.Match(actual, expression));

    [Theory]
    [InlineData("1.0-nightly")][InlineData("1.0-beta.1")][InlineData("1.0-beta-1")]
    [InlineData("1.0+build")][InlineData("1.0.0-20261029.120000-1")][InlineData("1.0-a")]
    [InlineData("1.0-final1")][InlineData("1.0-snapshot1")][InlineData("1.0preview1")]
    public void UnsupportedMavenShapesStayUnknown(string value)
    {
        Assert.Equal(ConstraintMatch.Unknown, MavenVersionConstraints.Match(value, "[1,2)"));
        Assert.Equal(ConstraintMatch.Unknown, MavenVersionConstraints.Match("1.5", "[" + value + ",2)"));
    }
    [Fact] public void PublishedQualifierOrderingHoldsAcrossTheSupportedSequence()
    {
        string[] order = ["1.0-alpha", "1.0-a1", "1.0-beta", "1.0-b1", "1.0-milestone", "1.0-m1", "1.0-rc", "1.0-rc1", "1.0-snapshot", "1.0", "1.0-sp", "1.0-sp1", "1.0.1"];
        for (int i = 0; i < order.Length; i++)
            for (int j = 0; j < order.Length; j++)
                Assert.Equal(i < j ? ConstraintMatch.Match : ConstraintMatch.Mismatch, MavenVersionConstraints.Match(order[i], "(," + order[j] + ")"));
    }
    [Fact] public void NumericQualifierSuffixesRemainBoundedAndUseNumericOrdering()
    {
        var suffix = new string('9', 900);
        Assert.Equal(ConstraintMatch.Match, MavenVersionConstraints.Match("1.0-rc" + suffix, "(1.0-rc2,1.0)"));
        Assert.Equal(ConstraintMatch.Unknown, MavenVersionConstraints.Match("1.0-rc" + new string('1', 1024), "[1,2)"));
        Assert.Equal(ConstraintMatch.Unknown, MavenVersionConstraints.Match("1.0", new string('[', 4097)));
    }
    [Theory] [InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public void NativeDependencyCheckUsesQualifiersAndIndependentDeclaredFml(ModLoader loader)
    {
        var profile = new GameProfile(Guid.NewGuid(), "Synthetic metadata", "1.21.1", loader, 2048, null, DateTimeOffset.UtcNow);
        var fml = loader == ModLoader.Forge ? "net.minecraftforge:fmlloader:1.21.1-52.0.1" : "net.neoforged.fancymodloader:loader:4.0.42";
        var receipt = new InstallationReceipt("1.21.1", 21, "test.Main", DateTimeOffset.UtcNow, [], [], [], [], [], "test", loader,
            loader == ModLoader.Forge ? "1.21.1-52.0.1" : "21.1.1", Forge: new(1, "synthetic", fml, Guid.NewGuid().ToString("N"), new("test", "", "SHA1", 0), []));
        ModInspection Mod(string id, string version, IReadOnlyList<ForgeModDependency> dependencies) => new(id + ".jar", "synthetic", 1, "fixture", id, id, version, loader, [], "*",
            ForgeMetadata: new("javafml", loader == ModLoader.Forge ? "[52,53)" : "[4,5)", [new(id, version, id, dependencies, new Dictionary<string, string>())]));
        var parent = Mod("parent", "1.0", [new("library", "[1.0-beta,1.0)", "required", "NONE", "BOTH")]);
        Assert.Empty(ForgeCompatibility.Check(profile, receipt, [parent, Mod("library", "1.0-rc1", [])]).Issues);
        Assert.Contains(ForgeCompatibility.Check(profile, receipt, [parent, Mod("library", "1.0", [])]).Issues, i => i.Code == "mods.dependency.version");
        Assert.Contains(ForgeCompatibility.Check(profile, receipt, [parent, Mod("library", "1.0-nightly", [])]).Issues, i => i.Code == "mods.constraint.unknown");
    }
}
