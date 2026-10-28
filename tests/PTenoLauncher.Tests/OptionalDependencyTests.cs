using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class OptionalDependencyTests
{
    private sealed class Catalog(params CatalogModVersion[] versions):IModCatalog
    {
        public List<string> Queries {get;}=[];
        public Task<CatalogProject> GetProjectAsync(string id,CancellationToken ct=default){Queries.Add("project:"+id);return Task.FromResult(new CatalogProject(id,id,id,true));}
        public Task<CatalogModVersion> GetVersionAsync(string id,CancellationToken ct=default){Queries.Add("version:"+id);return Task.FromResult(versions.FirstOrDefault(v=>v.Id==id)??throw new InvalidDataException("Missing version "+id));}
        public Task<IReadOnlyList<CatalogModVersion>> GetVersionsAsync(string id,string game,ModLoader loader,CancellationToken ct=default){Queries.Add("list:"+id);return Task.FromResult<IReadOnlyList<CatalogModVersion>>(versions.Where(v=>v.ProjectId==id).ToArray());}
    }
    private static CatalogModVersion V(string project,string id,params CatalogDependency[] deps)=>new(id,project,id,id,DateTimeOffset.UnixEpoch,"release",["1.21.1"],["fabric"],deps,new(project+".jar",new("https://cdn.modrinth.com/"+project+".jar"),10,new string('a',128)));
    private static GameProfile P=>new(Guid.NewGuid(),"Optional test","1.21.1",ModLoader.Fabric,2048,null,DateTimeOffset.UtcNow);
    private static CatalogDependency Opt(string? project,string? version=null)=>new(project,version,"optional");
    [Fact] public async Task OptionalIsNotRequestedOrAddedUntilExplicitlySelected()
    {
        var catalog=new Catalog(V("A","A1",Opt("O")),V("O","O1"));var resolver=new RemoteDependencyResolver(catalog);
        var initial=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[]);
        Assert.Single(initial.Entries);Assert.Single(initial.Optional);Assert.DoesNotContain(catalog.Queries,q=>q.EndsWith(":O"));
        var selected=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[initial.Optional[0].Key]);
        Assert.True(selected.CanInstall);Assert.Equal(2,selected.Entries.Count);Assert.Single(selected.SelectedOptional!);
        Assert.Contains(selected.Entries,e=>e.Version.ProjectId=="O" && e.Reason.Contains("Необов'язковий"));
    }
    [Fact] public async Task SelectedVersionOnlyOptionalPinsExactVersionAndResolvesItsRequiredDependencies()
    {
        var catalog=new Catalog(V("A","A1",Opt(null,"O1")),V("O","O1",new CatalogDependency("D",null,"required")),V("O","O2"),V("D","D1"));
        var resolver=new RemoteDependencyResolver(catalog);var initial=await resolver.ResolveAsync(P,[new("A")],[]);
        var result=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[initial.Optional[0].Key]);
        Assert.True(result.CanInstall);Assert.Contains(result.Entries,e=>e.Version.Id=="O1");Assert.DoesNotContain(result.Entries,e=>e.Version.Id=="O2");
        Assert.Contains(result.Entries,e=>e.Version.ProjectId=="D");
    }
    [Fact] public async Task UncheckingParentPrunesOrphanOptionalChildren()
    {
        var resolver=new RemoteDependencyResolver(new Catalog(V("A","A1",Opt("B")),V("B","B1",Opt("C")),V("C","C1")));
        var root=new OptionalDependencyKey("A1","B",null);var child=new OptionalDependencyKey("B1","C",null);
        var both=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[root,child]);Assert.Equal(3,both.Entries.Count);
        var removed=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[child]);Assert.Single(removed.Entries);Assert.Empty(removed.SelectedOptional!);
    }
    [Fact] public async Task ClearingOptionalKeepsComponentThatIsAlsoRequired()
    {
        var resolver=new RemoteDependencyResolver(new Catalog(V("A","A1",Opt("B"),new("B",null,"required")),V("B","B1")));
        var result=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[]);Assert.Equal(2,result.Entries.Count);
    }
    [Fact] public async Task ConflictingOptionalKeepsKnownChoiceForUserToUncheck()
    {
        var resolver=new RemoteDependencyResolver(new Catalog(V("A","A1",Opt("B")),V("B","B1",new CatalogDependency("A",null,"incompatible"))));
        var key=new OptionalDependencyKey("A1","B",null);
        var blocked=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[key]);Assert.False(blocked.CanInstall);
        Assert.Contains(blocked.Optional,o=>o.Key==key);Assert.Contains(key,blocked.SelectedOptional!);
        var fixedPlan=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[]);Assert.True(fixedPlan.CanInstall);Assert.Single(fixedPlan.Entries);
    }
    [Fact] public async Task MissingFileOnlyIdentityNeverGuessesAProject()
    {
        var catalog=new Catalog(V("A","A1",new CatalogDependency(null,null,"optional","unknown.jar")));var resolver=new RemoteDependencyResolver(catalog);
        var result=await resolver.ResolveAsync(P,[new("A")],[]);var option=Assert.Single(result.Optional);Assert.False(option.CanSelect);
        var blocked=await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[option.Key]);Assert.False(blocked.CanInstall);
        Assert.DoesNotContain(catalog.Queries,q=>q.Contains("unknown"));
    }
    [Fact] public async Task StaleParentVersionSelectionIsDiscardedWithoutFetchingOptional()
    {
        var catalog=new Catalog(V("A","A2",Opt("B")),V("B","B1"));var resolver=new RemoteDependencyResolver(catalog);
        var result=await resolver.ResolveWithOptionalAsync(P,[new("A","A2")],[],[new("A1","B",null)]);
        Assert.True(result.CanInstall);Assert.Empty(result.SelectedOptional!);Assert.Single(result.Entries);
        Assert.DoesNotContain(catalog.Queries,q=>q.EndsWith(":B"));
    }
    [Fact] public async Task WrongMinecraftOptionalIsBlockedAndInstalledVersionIsNotReplaced()
    {
        var a=V("A","A1",Opt("B","B2"));var b=V("B","B2") with{GameVersions=["1.22"]};
        var resolver=new RemoteDependencyResolver(new Catalog(a,b,V("B","B1")));var key=new OptionalDependencyKey("A1","B","B2");
        Assert.False((await resolver.ResolveWithOptionalAsync(P,[new("A")],[],[key])).CanInstall);
        Assert.False((await resolver.ResolveWithOptionalAsync(P,[new("A")],[new(V("B","B1"))],[key])).CanInstall);
    }
    [Fact] public async Task MissingVersionBlocksWithoutLosingKnownChoiceAndCyclesTerminate()
    {
        var bad=new RemoteDependencyResolver(new Catalog(V("A","A1",Opt(null,"absent"))));
        var result=await bad.ResolveWithOptionalAsync(P,[new("A")],[],[new("A1",null,"absent")]);Assert.False(result.CanInstall);Assert.Single(result.Optional);
        var cycle=new RemoteDependencyResolver(new Catalog(V("A","A1",Opt("B")),V("B","B1",Opt("A"))));
        var resolved=await cycle.ResolveWithOptionalAsync(P,[new("A")],[],[new("A1","B",null),new("B1","A",null)]);
        Assert.True(resolved.CanInstall);Assert.Equal(2,resolved.Entries.Count);
    }
}
