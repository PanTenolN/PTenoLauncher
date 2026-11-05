using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Testing;

namespace PTenoLauncher.IntegrationSmoke;

public static class AiNativeProbe
{
    public static async Task RunAsync()
    {
        foreach(var tab in new[]{"AiConnectionTab","AiAssistantTab","MyTranslationsTab"})
        {
            using var owned=new OwnedTestRoot("ai-native",64L*1024*1024);
            try
            {
                var paths=new LauncherPaths(owned.Root);paths.Initialize();var store=new SqliteLauncherStore(paths);await store.InitializeAsync();
                await store.SetSettingAsync("ui.language","uk");await store.SetSettingAsync("updates.auto","false");await store.SetSettingAsync("network.offline","true");await store.SetSettingAsync("ai.enabled","true");
                var p=new GameProfile(Guid.NewGuid(),"Synthetic translation profile","1.21",ModLoader.Vanilla,2048,null,DateTimeOffset.UtcNow);await store.SaveProfileAsync(p);
                using var http=new HttpClient(new NoNetwork());var policy=new AiPolicy();var translations=new TranslationWorkspace(paths,store,new(paths),policy,new(http,policy,new(){Offline=true},()=>throw new IOException("No test key")));
                var jar=Path.Combine(owned.Root,"synthetic.jar");
                using(var z=System.IO.Compression.ZipFile.Open(jar,System.IO.Compression.ZipArchiveMode.Create)){using var s=z.CreateEntry("assets/synthetic/lang/fr_fr.json").Open();JsonSerializer.Serialize(s,new Dictionary<string,string>{{"hello","Bonjour %s"}});}
                await translations.ImportAsync(p,[jar],"uk_ua",sourceLocale:"fr_fr");await translations.SaveManualAsync(p,"uk_ua","synthetic",new Dictionary<string,string>{{"hello","Вітаю %s"}},sourceLocale:"fr_fr");
                SqliteConnection.ClearAllPools();
                var dotnet=Path.Combine(OwnedTestRoot.ProjectRoot,".tools","dotnet","dotnet.exe");
                var app=Path.Combine(OwnedTestRoot.ProjectRoot,"src","PTenoLauncher.Desktop","bin","Release","net10.0","PTenoLauncher.dll");
                var start=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=owned.Root};
                foreach(var a in new[]{app,"--data-root",owned.Root,"--smoke-test","--smoke-page","Settings","--smoke-settings-tab",tab,"--smoke-size","980","680","--capture-ui"})start.ArgumentList.Add(a);
                using var process=Process.Start(start)??throw new IOException("Cannot start owned native UI probe.");
                var identity=owned.RegisterProcess(process);
                await AtomicFile.WriteJsonAsync(Path.Combine(owned.Root,"probe-process.json"),new{identity,tab,owned.Root});
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(40));
                try{await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){owned.Retain("Exact native UI process did not close; no forced termination: PID "+identity.Pid);throw;}
                if(process.ExitCode!=0||!File.Exists(Path.Combine(owned.Root,"window-preview.png")))throw new IOException("Native UI probe failed: "+tab);
                var smoke=await File.ReadAllTextAsync(Path.Combine(owned.Root,"gui-smoke.txt"));if(!smoke.Contains("initialized=True",StringComparison.Ordinal))throw new IOException("Native initialization not confirmed.");
                await AtomicFile.WriteJsonAsync(Path.Combine(owned.Root,"native-result.json"),new{tab,state="PASSED",process.ExitCode,offlineSetting=true,gameplay=false,liveAi=false,logicalWidth=980,logicalHeight=680,smoke});
                Console.WriteLine("Native UI evidence: "+owned.Evidence);
            }
            catch(Exception error) { await File.WriteAllTextAsync(Path.Combine(owned.Root,"native-failure.log"),error.ToString());throw; }
            finally { SqliteConnection.ClearAllPools();try{owned.PreserveRunnerEvidence();}catch(Exception e){owned.Retain("Native evidence error: "+e.Message);throw;}owned.CheckBudget(); }
        }
    }
    private sealed class NoNetwork:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>throw new IOException("No native probe HTTP allowed.");}
}
