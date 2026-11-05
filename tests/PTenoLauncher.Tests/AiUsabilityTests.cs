using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class AiUxFixture : IDisposable
{
    public TestWorkspace Workspace { get; } = new();
    public AiPolicy Policy { get; } = new();
    public NetworkPolicy Network { get; } = new();
    public int Calls, KeyReads;
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond;
    public OpenAiClient Client { get; }
    public TranslationWorkspace Translations { get; }
    private readonly HttpClient http;
    public AiUxFixture()
    { http = new(Transport()); Client = new(http, Policy, Network, () => { KeyReads++; return "synthetic-api-key-only"; }); Translations = new(Workspace.Paths, Workspace.Store, new(Workspace.Paths), Policy, Client); }
    public HttpMessageHandler Transport() => new Handler(async (request, ct) => { Calls++; return Respond is null ? request.Method == HttpMethod.Get ? new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"fixture-model\"}]}") } : Completed("Fixture response only") : await Respond(request, ct); });
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => callback(request, ct); }
    public static HttpResponseMessage Completed(string text) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text } } } } })) };
    public string Jar(string locale = "fr_fr", string text = "Bonjour %s", string ns = "demo", string? secondLocale = null)
    {
        var path = Path.Combine(Workspace.Paths.Root, Guid.NewGuid().ToString("N") + ".jar"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry($"assets/{ns}/lang/{locale}.json").Open()) JsonSerializer.Serialize(s, new Dictionary<string,string> { ["hello"] = text });
        if (secondLocale is not null) { using var s = zip.CreateEntry($"assets/{ns}/lang/{secondLocale}.json").Open(); JsonSerializer.Serialize(s, new Dictionary<string,string> { ["hello"] = "Hello %s" }); }
        return path;
    }
    public void Dispose() { http.Dispose(); Workspace.Dispose(); }
}
public sealed class AiUsabilityTests
{
    [Fact] public async Task ConnectionNeedsExplicitConsentAiAndNetworkBeforeReadingAnyKey()
    {
        using var f = new AiUxFixture(); await Assert.ThrowsAsync<InvalidOperationException>(() => f.Client.CheckConnectionAsync(true));
        f.Policy.SetEnabled(true); await Assert.ThrowsAsync<AiServiceException>(() => f.Client.CheckConnectionAsync(false)); f.Network.Offline = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Client.CheckConnectionAsync(true)); Assert.Equal(0, f.Calls); Assert.Equal(0, f.KeyReads);
        f.Network.Offline = false; Assert.Equal("fixture-model", Assert.Single(await f.Client.CheckConnectionAsync(true))); Assert.Equal(1, f.Calls); Assert.Equal(1, f.KeyReads);
    }
    [Theory][InlineData(401,"AiUxBadKey")][InlineData(403,"AiUxPermission")][InlineData(404,"AiUxBadModel")][InlineData(429,"AiUxLimit")][InlineData(503,"AiUxProvider")]
    public async Task ErrorResponsesExposeSafeCodesNotBodiesOrSecrets(int code, string expected)
    {
        using var f = new AiUxFixture(); f.Policy.SetEnabled(true); f.Respond = (_,_) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("private-body sk-secret-should-not-display") });
        var error = await Assert.ThrowsAsync<AiServiceException>(() => f.Client.AskWithContextAsync("fixture-model", "question", [], true)); Assert.Equal(expected, error.Code); Assert.DoesNotContain("private", error.ToString()); Assert.Equal(1, f.Calls);
    }
    [Fact] public async Task BoundedSessionContextIsActuallySentRedactedWithNoStoreOrTools()
    {
        using var f = new AiUxFixture(); f.Policy.SetEnabled(true);
        f.Respond = async (r,ct) => { using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct)); var root = json.RootElement; Assert.False(root.GetProperty("store").GetBoolean()); Assert.False(root.TryGetProperty("tools", out _)); var messages = root.GetProperty("input"); Assert.Equal(3,messages.GetArrayLength()); Assert.Equal("assistant",messages[1].GetProperty("role").GetString()); Assert.DoesNotContain("person@example.com",messages[0].GetProperty("content").GetString()!); Assert.DoesNotContain("secret",messages[2].GetProperty("content").GetString()!); return AiUxFixture.Completed("response"); };
        await f.Client.AskWithContextAsync("fixture-model","Bearer secret",[new("user","person@example.com"),new("assistant","earlier response")],true);
        await Assert.ThrowsAsync<AiServiceException>(() => f.Client.AskWithContextAsync("fixture-model","question",Enumerable.Range(0,13).Select(_=>new AiTurn("user","x")).ToArray(),true)); Assert.Equal(1,f.Calls);
    }
    [Fact] public async Task DirectionalImportUsesActualNonEnglishBytesAndPreservesIndependentManualEdits()
    {
        using var f = new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var profile=f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(profile); var jar=f.Jar(secondLocale:"en_us");
        Assert.Equal(new[]{"en_us","fr_fr"},Assert.Single(await f.Translations.DiscoverAsync([jar])).Locales);
        await f.Translations.ImportAsync(profile,[jar],"uk_ua",sourceLocale:"fr_fr"); await f.Translations.SaveManualAsync(profile,"uk_ua","demo",new Dictionary<string,string>{{"hello","Вітаю %s"}},sourceLocale:"fr_fr");
        await f.Translations.ImportAsync(profile,[jar],"uk_ua"); var french=(await f.Translations.ReadAsync(profile.Id,"uk_ua","demo",sourceLocale:"fr_fr"))!; var english=(await f.Translations.ReadAsync(profile.Id,"uk_ua","demo"))!;
        Assert.Equal("Bonjour %s",french.Sources["hello"]); Assert.Equal("Hello %s",english.Sources["hello"]); Assert.Equal("Вітаю %s",french.Values["hello"].Value); Assert.Empty(english.Values); Assert.Equal(2,(await f.Translations.LibraryAsync(profile.Id)).Count); Assert.Equal(0,f.Calls); Assert.Equal(0,f.KeyReads);
    }
    [Fact] public async Task MissingLanguageInAnySelectedModDoesNotSilentlyFallbackOrWritePartialDrafts()
    {
        using var f=new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p=f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(p);
        var error=await Assert.ThrowsAsync<AiServiceException>(()=>f.Translations.ImportAsync(p,[f.Jar(),f.Jar("en_us",ns:"other")],"uk_ua",sourceLocale:"fr_fr")); Assert.Equal("AiUxSourceMissing",error.Code); Assert.Empty(await f.Translations.LibraryAsync(p.Id)); Assert.Equal(0,f.Calls);
    }
    [Fact] public async Task LegacyMigrationHasByteExactBackupAndKeepsManualReviewStateAcrossRestart()
    {
        using var f=new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p=f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(p);
        var legacy=Path.Combine(f.Workspace.Paths.Profile(p.Id),"translations","uk_ua","demo.json"); Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var raw=JsonSerializer.SerializeToUtf8Bytes(new { @namespace="demo",locale="uk_ua",sources=new Dictionary<string,string>{{"hello","Hello %s"}},values=new Dictionary<string,TranslatedValue>{{"hello",new("Hello %s","Привіт %s",true,true)}}},AtomicFile.JsonOptions); await File.WriteAllBytesAsync(legacy,raw);
        var d=(await f.Translations.ReadAsync(p.Id,"uk_ua","demo"))!; Assert.Equal("en_us",d.SourceLocale); Assert.True(d.Values["hello"].Manual); Assert.True(d.Values["hello"].NeedsReview); Assert.Equal(raw,await File.ReadAllBytesAsync(legacy)); Assert.Equal(raw,await File.ReadAllBytesAsync(legacy+".legacy-v1-backup"));
        var restarted=new TranslationWorkspace(f.Workspace.Paths,f.Workspace.Store,new(f.Workspace.Paths),f.Policy,f.Client); Assert.Single(await restarted.LibraryAsync(p.Id));
        var changed=f.Jar("en_us","New source %s"); await restarted.ImportAsync(p,[changed],"uk_ua"); Assert.Equal("Привіт %s",(await restarted.ReadAsync(p.Id,"uk_ua","demo"))!.Values["hello"].Value);
    }
    [Fact] public async Task NewDirectionPromptSameLanguageNoopAndIncrementalBatchesRespectSources()
    {
        using var f=new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p=f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(p); var jar=f.Jar(); await f.Translations.ImportAsync(p,[jar],"uk_ua",sourceLocale:"fr_fr");
        f.Policy.SetEnabled(true); f.Respond=async(r,ct)=>{ using var doc=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct)); Assert.Contains("from fr_fr into uk_ua",doc.RootElement.GetProperty("instructions").GetString()); Assert.Contains("Bonjour",doc.RootElement.GetProperty("input").GetString()); return AiUxFixture.Completed("{\"translations\":{\"hello\":\"Привіт %s\"}}"); };
        await f.Translations.TranslateAsync(p,"uk_ua","fixture-model",true,sourceLocale:"fr_fr"); await f.Translations.TranslateAsync(p,"uk_ua","fixture-model",true,sourceLocale:"fr_fr"); Assert.Equal(1,f.Calls);
        f.Policy.SetEnabled(false); f.Network.Offline=true; await f.Translations.TranslateAsync(p,"fr_fr","fixture-model",false,sourceLocale:"fr_fr"); Assert.Equal(1,f.Calls);
    }
    [Fact] public async Task OfflineManualExportSeparatesDirectionsAndStaleEditorCannotOverwriteNewerBytes()
    {
        using var f=new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p=f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(p); f.Network.Offline=true;
        await f.Translations.ImportAsync(p,[f.Jar()],"uk_ua",sourceLocale:"fr_fr"); var d=(await f.Translations.ReadAsync(p.Id,"uk_ua","demo",sourceLocale:"fr_fr"))!; var hash=TranslationWorkspace.Fingerprint(d);
        await f.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","Вітаю %s"}},sourceLocale:"fr_fr",expectedFingerprint:hash);
        await Assert.ThrowsAsync<AiServiceException>(()=>f.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","Нове %s"}},sourceLocale:"fr_fr",expectedFingerprint:hash));
        var pack=await f.Translations.ExportAsync(p,"uk_ua",34,sourceLocale:"fr_fr"); Assert.EndsWith("fr_fr-to-uk_ua.zip",pack.Path); using var z=ZipFile.OpenRead(pack.Path); using var json=JsonDocument.Parse(z.GetEntry("assets/demo/lang/uk_ua.json")!.Open()); Assert.Equal("Вітаю %s",json.RootElement.GetProperty("hello").GetString()); Assert.Equal(0,f.Calls); Assert.Equal(0,f.KeyReads);
    }
    [Theory][InlineData("1.20",15)][InlineData("1.21",34)]
    public async Task ExactReleaseCatalogUsesVerifiedEntriesOnly(string version,int expected)
    { using var f=new AiUxFixture(); var p=f.Workspace.Profile() with { MinecraftVersion=version }; Assert.Equal(expected,(await TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths,p)).Format); await Assert.ThrowsAsync<AiServiceException>(()=>TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths,p with { MinecraftVersion=version+".unknown" })); Assert.Equal(0,f.Calls); }
    [Fact] public async Task VerifiedClientVersionMetadataSupportsExactUnknownCatalogVersionButChangedJarDoesNot()
    {
        using var f=new AiUxFixture(); var p=f.Workspace.Profile() with { MinecraftVersion="fixture-1" }; var relative="objects/sha1/ab/fixture.jar"; var jar=SafePath.Join(f.Workspace.Paths.Cache,relative); Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
        using(var z=ZipFile.Open(jar,ZipArchiveMode.Create)){using var s=z.CreateEntry("version.json").Open();JsonSerializer.Serialize(s,new { id="fixture-1",pack_version=new { resource=34 }});}
        var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(jar))); var receipt=new InstallationReceipt(p.MinecraftVersion,21,"fixture.Main",DateTimeOffset.UtcNow,[new(relative,hash,"SHA256",new FileInfo(jar).Length)],[relative],[],[],[],"fixture"); await AtomicFile.WriteJsonAsync(f.Workspace.Paths.Receipt(p.Id),receipt);
        Assert.Equal(34,(await TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths,p)).Format); await File.AppendAllTextAsync(jar,"changed"); await Assert.ThrowsAsync<AiServiceException>(()=>TranslationPackMetadataResolver.ResolveAsync(f.Workspace.Paths,p));
    }
    [Fact] public async Task DisabledAiCancelsContextRequestAndNoDraftIsStoredAsChatHistory()
    { using var f=new AiUxFixture(); f.Policy.SetEnabled(true); var entered=new TaskCompletionSource(); f.Respond=async(_,ct)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,ct);return AiUxFixture.Completed("never");};var task=f.Client.AskWithContextAsync("fixture-model","question",[],true);await entered.Task;f.Policy.SetEnabled(false);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);Assert.Empty(Directory.GetFiles(f.Workspace.Paths.Root,"*chat*",SearchOption.AllDirectories)); }
}
