using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class RemoteDependencyTests
{
    private sealed class Catalog(params CatalogModVersion[] versions) : IModCatalog
    {
        public Task<CatalogProject> GetProjectAsync(string id, CancellationToken ct = default) => Task.FromResult(new CatalogProject(id,id,id,true));
        public Task<CatalogModVersion> GetVersionAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(versions.FirstOrDefault(v => v.Id == id) ?? throw new InvalidDataException("missing " + id));
        public Task<IReadOnlyList<CatalogModVersion>> GetVersionsAsync(string id, string minecraft, ModLoader loader, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogModVersion>>(versions.Where(v => v.ProjectId == id).ToArray());
    }
    private static CatalogModVersion V(string project, string id, params CatalogDependency[] dependencies) =>
        new(id,project,id,id,DateTimeOffset.UnixEpoch.AddSeconds(id.LastOrDefault() == '2' ? 2 : 1),"release",["1.21.1"],["fabric"],dependencies,
            new(project+".jar",new("https://cdn.modrinth.com/data/"+project+".jar"),10,new string('a',128)));
    private static CatalogDependency D(string project, string? version=null, string kind="required") => new(project,version,kind);
    private static GameProfile Profile => new(Guid.NewGuid(),"Resolver","1.21.1",ModLoader.Fabric,2048,null,DateTimeOffset.UtcNow);
    [Fact] public async Task RecursesAndSeparatesOptionalWithoutAi()
    {
        var result=await new RemoteDependencyResolver(new Catalog(V("A","A1",D("B"),D("O",kind:"optional")),V("B","B1",D("C")),V("C","C1")))
            .ResolveAsync(Profile,[new("A")],[]);
        Assert.True(result.CanInstall); Assert.Equal(3,result.Entries.Count);Assert.Single(result.Optional);
        Assert.DoesNotContain(result.Entries,e=>e.Version.ProjectId=="O");
        Assert.Contains(result.Entries,e=>e.Version.ProjectId=="C" && e.Reason.Contains("B"));
    }
    [Fact] public async Task BacktracksWhenNewestTransitiveChoiceConflictsWithPinnedVersion()
    {
        var catalog=new Catalog(V("A","A1",D("B"),D("C")),V("B","B2",D("D","D2")),V("B","B1",D("D","D1")),
            V("C","C1",D("D","D1")),V("D","D1"),V("D","D2"));
        var result=await new RemoteDependencyResolver(catalog).ResolveAsync(Profile,[new("A")],[]);
        Assert.True(result.CanInstall);Assert.Contains(result.Entries,e=>e.Version.Id=="B1");Assert.Contains(result.Entries,e=>e.Version.Id=="D1");
    }
    [Fact] public async Task MissingMetadataOnNewestCandidateAllowsOlderValidCandidate()
    {
        var result=await new RemoteDependencyResolver(new Catalog(V("A","A2",D("B","missing")),V("A","A1")))
            .ResolveAsync(Profile,[new("A")],[]);
        Assert.True(result.CanInstall);Assert.Equal("A1",Assert.Single(result.Entries).Version.Id);
    }
    [Fact] public async Task CyclesTerminateAndDuplicateRootsDoNotDuplicateFiles()
    {
        var result=await new RemoteDependencyResolver(new Catalog(V("A","A1",D("B")),V("B","B1",D("A"))))
            .ResolveAsync(Profile,[new("A"),new("A")],[]);
        Assert.True(result.CanInstall);Assert.Equal(2,result.Entries.Count);
    }
    [Fact] public async Task ConflictingExactRootsAreBlocked()
    {
        var result=await new RemoteDependencyResolver(new Catalog(V("A","A1"),V("A","A2")))
            .ResolveAsync(Profile,[new("A","A1"),new("A","A2")],[]);
        Assert.False(result.CanInstall);Assert.NotEmpty(result.Blockers);
    }
    [Fact] public async Task IncompatibleInstalledModBlocksAndIsNeverOverwritten()
    {
        var result=await new RemoteDependencyResolver(new Catalog(V("A","A1",D("B",kind:"incompatible")),V("B","B1")))
            .ResolveAsync(Profile,[new("A")],[new(V("B","B1"))]);
        Assert.False(result.CanInstall);Assert.Contains(result.Blockers,b=>b.Contains("Несумісність"));
    }
    [Fact] public async Task InstalledRequiredVersionIsReusedAndDifferentPinIsBlocked()
    {
        var catalog=new Catalog(V("A","A1",D("B")),V("B","B1"),V("B","B2"));
        var reused=await new RemoteDependencyResolver(catalog).ResolveAsync(Profile,[new("A")],[new(V("B","B1"))]);
        Assert.True(reused.CanInstall);Assert.True(reused.Entries.Single(e=>e.Version.ProjectId=="B").AlreadyInstalled);
        var blocked=await new RemoteDependencyResolver(catalog).ResolveAsync(Profile,[new("B","B2")],[new(V("B","B1"))]);
        Assert.False(blocked.CanInstall);
    }
    [Fact] public async Task WrongMinecraftOrLoaderIsRejectedEvenForExactVersion()
    {
        var bad=V("A","A1") with {GameVersions=["1.20.1"],Loaders=["forge"]};
        var result=await new RemoteDependencyResolver(new Catalog(bad)).ResolveAsync(Profile,[new("A","A1")],[]);
        Assert.False(result.CanInstall);
    }
    [Fact] public async Task FileOnlyRequiredDependencyIsUnknownAndBlocks()
    {
        var result=await new RemoteDependencyResolver(new Catalog(V("A","A1",new CatalogDependency(null,null,"required","external.jar"))))
            .ResolveAsync(Profile,[new("A")],[]);
        Assert.False(result.CanInstall);Assert.Contains(result.Blockers,b=>b.Contains("неможливо"));
    }
}
