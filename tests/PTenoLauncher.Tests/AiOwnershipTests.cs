using System.Net;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class AiOwnershipTests
{
    public class FailingRegistration : System.Reflection.DispatchProxy
    {
        public PTenoLauncher.Core.ILauncherStore Inner=null!;public string Mode="";
        protected override object? Invoke(System.Reflection.MethodInfo? method,object?[]? args)
        {
            if(method!.Name=="RegisterArtifactAsync"&&Mode.Length>0)
            {
                var mode=Mode;Mode="";return mode=="cancel"?Task.FromCanceled(new CancellationToken(true)):Task.FromException(new IOException("Controlled ownership commit failure"));
            }
            return method.Invoke(Inner,args);
        }
    }
    [Theory][InlineData("io")][InlineData("cancel")]
    public async Task InterruptedOwnershipPublicationRetainsJournalAndRecoversVerifiedPack(string mode)
    {
        using var f=new AiUxFixture();await f.Workspace.Store.InitializeAsync();var p=f.Workspace.Profile();await f.Workspace.Store.SaveProfileAsync(p);
        await f.Translations.ImportAsync(p,[f.Jar()],"uk_ua",sourceLocale:"fr_fr");await f.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","Saved %s"}},sourceLocale:"fr_fr");
        var proxy=System.Reflection.DispatchProxy.Create<PTenoLauncher.Core.ILauncherStore,FailingRegistration>();var failure=(FailingRegistration)(object)proxy;failure.Inner=f.Workspace.Store;failure.Mode=mode;
        var workspace=new TranslationWorkspace(f.Workspace.Paths,proxy,new(f.Workspace.Paths),f.Policy,f.Client);
        if(mode=="io")await Assert.ThrowsAsync<IOException>(()=>workspace.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr"));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>workspace.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr"));
        var pending=SafePath.Join(f.Workspace.Paths.Profile(p.Id),"translations/generated/fr_fr/uk_ua/receipt.json.pending");Assert.True(File.Exists(pending));
        var recovered=await workspace.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr");Assert.True(File.Exists(recovered.Path));Assert.False(File.Exists(pending));Assert.Single(await f.Workspace.Store.GetArtifactsAsync(p.Id));Assert.Equal(0,f.Calls);
    }
    [Fact] public async Task AllCanonicalDirectionsAreDistinctAndDeletionRecognizesThem()
    {
        using var f = new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p = f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(p);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in TranslationRules.Locales) foreach (var target in TranslationRules.Locales)
        {
            var path = TranslationPackOwnership.RelativePath(f.Workspace.Paths,p.Id,source,target); Assert.True(names.Add(path));
            await f.Workspace.Store.RegisterArtifactAsync(new(p.Id,path,"translation-resourcepack"));
        }
        Assert.Equal(64,names.Count);
        var deletion = new ProfileDeletionService(f.Workspace.Paths,f.Workspace.Store,new(f.Workspace.Paths));
        Assert.NotNull(await deletion.PreviewAsync(p.Id,false,[]));
        Assert.EndsWith("pteno-translations-uk_ua.zip",TranslationPackOwnership.RelativePath(f.Workspace.Paths,p.Id,"en_us","uk_ua"));
        foreach (var bad in new[] { "fr_FR", "../fr_fr", "fr_fr-to-uk_ua", "xx_xx", "fr_fr/uk_ua" }) Assert.Throws<ArgumentException>(()=>TranslationPackOwnership.RelativePath(f.Workspace.Paths,p.Id,bad,"uk_ua"));
        foreach (var bad in new[] { TranslationPackOwnership.RelativePath(f.Workspace.Paths,Guid.NewGuid(),"fr_fr","uk_ua"), $"instances/{p.Id:N}/game/resourcepacks/pteno-translations-en_us-to-uk_ua.zip", $"instances/{p.Id:N}/game/resourcepacks/foreign.zip" })
            await Assert.ThrowsAsync<InvalidDataException>(()=>f.Workspace.Store.RegisterArtifactAsync(new(p.Id,bad,"translation-resourcepack")));
    }
    [Fact] public async Task ReexportBacksUpExactPreviousPackAndRejectsChangedOrUntrackedBytes()
    {
        using var f = new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p=f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(p);
        await f.Translations.ImportAsync(p,[f.Jar()],"uk_ua",sourceLocale:"fr_fr");
        await f.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","First %s"}},sourceLocale:"fr_fr");
        var first=await f.Translations.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr"); var original=await File.ReadAllBytesAsync(first.Path);
        Assert.Single(await f.Translations.LibraryAsync(p.Id));
        await f.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","Second %s"}},sourceLocale:"fr_fr");
        await f.Translations.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr");
        Assert.Equal(original,await File.ReadAllBytesAsync(SafePath.Join(f.Workspace.Paths.Profile(p.Id),"translations/v2/fr_fr/uk_ua/previous-pack.zip")));
        await File.AppendAllTextAsync(first.Path,"user alteration"); var changed=await File.ReadAllBytesAsync(first.Path);
        await Assert.ThrowsAsync<IOException>(()=>f.Translations.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr")); Assert.Equal(changed,await File.ReadAllBytesAsync(first.Path));
        var other=TranslationPackOwnership.RelativePath(f.Workspace.Paths,p.Id,"fr_fr","de_de"); var untracked=SafePath.Join(f.Workspace.Paths.Root,other); await File.WriteAllTextAsync(untracked,"USER");
        await f.Translations.ImportAsync(p,[f.Jar()],"de_de",sourceLocale:"fr_fr"); await f.Translations.SaveManualAsync(p,"de_de","demo",new Dictionary<string,string>{{"hello","Hallo %s"}},sourceLocale:"fr_fr");
        await Assert.ThrowsAsync<IOException>(()=>f.Translations.ExportAsync(p,"de_de",34,sourceLocale:"fr_fr")); Assert.Equal("USER",await File.ReadAllTextAsync(untracked)); Assert.Equal(0,f.Calls);
    }
    [Fact] public async Task RestrictedCatalogDoesNotMakeResponsesOnlyKeyInvalid()
    {
        using var f=new AiUxFixture(); f.Policy.SetEnabled(true);
        f.Respond=(r,_)=>Task.FromResult(r.Method==HttpMethod.Get?new HttpResponseMessage(HttpStatusCode.Forbidden):AiUxFixture.Completed("Responses works"));
        Assert.Equal("AiUxCatalogDenied",(await Assert.ThrowsAsync<AiServiceException>(()=>f.Client.CheckConnectionAsync(true))).Code);
        Assert.Equal("Responses works",await f.Client.AskWithContextAsync("fixture-model","question",[],true)); Assert.Equal(2,f.Calls);
    }
}
