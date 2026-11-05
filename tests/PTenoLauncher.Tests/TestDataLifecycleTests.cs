using System.Text.Json;
using PTenoLauncher.Testing;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class TestDataLifecycleTests
{
    [Fact] public void EmptyPartialCleanupCanRecoverButMissingMarkerCannotAdoptNewBytes()
    {
        var data=new OwnedTestRoot("partial-cleanup");var file=Path.Combine(data.Root,"owned-test-root.json");var raw=File.ReadAllText(file);var marker=JsonSerializer.Deserialize<OwnedTestRoot.Marker>(raw)!;
        File.Delete(file);File.WriteAllText(Path.Combine(data.Root,"unknown"),"NEWER BYTES");
        Assert.Throws<IOException>(()=>data.Dispose());Assert.Equal("NEWER BYTES",File.ReadAllText(Path.Combine(data.Root,"unknown")));
        File.Delete(Path.Combine(data.Root,"unknown"));OwnedTestRoot.CleanupOwned(marker,data.Evidence);Assert.False(Directory.Exists(data.Root));data.Dispose();
    }
    [Theory][InlineData("success",0)][InlineData("failure",1)][InlineData("cancel",2)][InlineData("abandon",0)]
    public async Task ExecutableRunnerCleansAllOutcomesAndRecoversAbandonedOwnedRoot(string mode,int expected)
    {
        var dotnet=Path.Combine(OwnedTestRoot.ProjectRoot,".tools","dotnet","dotnet.exe");
        async Task<(int Exit,string Output)> Run(string dll,params string[] args)
        {
            var start=new System.Diagnostics.ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=OwnedTestRoot.ProjectRoot};
            start.ArgumentList.Add(Path.Combine(OwnedTestRoot.ProjectRoot,dll));foreach(var a in args)start.ArgumentList.Add(a);
            using var p=System.Diagnostics.Process.Start(start)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));await p.WaitForExitAsync(limit.Token);return(p.ExitCode,await output+await error);
        }
        var result=await Run("tests/PTenoLauncher.UpdaterE2E/bin/Release/net10.0/PTenoLauncher.UpdaterE2E.dll","--lifecycle-probe",mode);Assert.Equal(expected,result.Exit);
        using var json=JsonDocument.Parse(result.Output);var root=json.RootElement.GetProperty("root").GetString()!;var evidence=json.RootElement.GetProperty("evidence").GetString()!;
        Assert.True(File.Exists(Path.Combine(evidence,"records","failure.log")));
        if(mode=="abandon")
        {
            Assert.True(Directory.Exists(root));var recovery=await Run("tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll","--owned-cleanup",root);Assert.True(recovery.Exit==0,recovery.Output);
        }
        Assert.False(Directory.Exists(root));
    }
    [Theory][InlineData("success")][InlineData("failure")][InlineData("cancel")]
    public void FinallyCleanupPreservesEvidenceForEveryOutcomeAndRepeatedRun(string outcome)
    {
        for(var i=0;i<2;i++)
        {
            var data=new OwnedTestRoot("lifecycle"); var path=data.Root;
            try
            {
                File.WriteAllBytes(Path.Combine(path,"payload.bin"),new byte[1024*1024]);
                File.WriteAllText(Path.Combine(path,"failure.log"),outcome); data.Preserve("failure.log"); data.CheckBudget();
                if(outcome=="failure") throw new InvalidDataException("original failure");
                if(outcome=="cancel") throw new OperationCanceledException();
            }
            catch(Exception e) when(e is InvalidDataException or OperationCanceledException) { Assert.Equal(outcome=="failure",e is InvalidDataException); }
            finally { data.Dispose(); }
            Assert.False(Directory.Exists(path)); Assert.Equal(outcome,File.ReadAllText(Path.Combine(data.Evidence,"records","failure.log")));
            using var report=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(data.Evidence,"cleanup-*.json").Single()));
            Assert.Equal(0,report.RootElement.GetProperty("RetainedBytes").GetInt64()); Assert.True(report.RootElement.GetProperty("RemovedBytes").GetInt64()>=1024*1024);
        }
    }
    [Fact] public void ChangedOwnerMarkerIsRetainedAndCleanupFailureIsVisible()
    {
        var data=new OwnedTestRoot("owner-change"); var file=Path.Combine(data.Root,"owned-test-root.json"); var original=File.ReadAllText(file);
        File.WriteAllText(file,"{}"); Assert.Throws<IOException>(()=>data.Dispose()); Assert.True(Directory.Exists(data.Root));
        File.WriteAllText(file,original); OwnedTestRoot.CleanupOwned(JsonSerializer.Deserialize<OwnedTestRoot.Marker>(original)!,data.Evidence); Assert.False(Directory.Exists(data.Root));
        Assert.Equal(2,Directory.GetFiles(data.Evidence,"cleanup-*.json").Length);
    }
    [Fact] public void BusyFileIsRetainedUntilHandleIsReleasedAndUnknownRootIsRefused()
    {
        var data=new OwnedTestRoot("busy-file"); var marker=JsonSerializer.Deserialize<OwnedTestRoot.Marker>(File.ReadAllText(Path.Combine(data.Root,"owned-test-root.json")))!;
        var file=Path.Combine(data.Root,"busy.bin"); File.WriteAllText(file,"retain");
        using(var handle=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Assert.Throws<IOException>(()=>data.Dispose());
        Assert.True(File.Exists(file)); Assert.Throws<IOException>(()=>OwnedTestRoot.CleanupOwned(marker with {Root=Path.GetDirectoryName(marker.Root)!},data.Evidence));
        OwnedTestRoot.CleanupOwned(marker,data.Evidence); Assert.False(Directory.Exists(data.Root));
    }
    [Fact] public void BudgetExceededIsExplicitAndDoesNotPreventFinallyCleanup()
    {
        var data=new OwnedTestRoot("budget",1024); try { File.WriteAllBytes(Path.Combine(data.Root,"payload"),new byte[2048]); Assert.Throws<IOException>(()=>data.CheckBudget()); } finally { data.Dispose(); }
        Assert.False(Directory.Exists(data.Root));
    }
}
