using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Testing;

/// <summary>Test-only owned payloads. Compact evidence lives outside disposable data.</summary>
public sealed class OwnedTestRoot : IDisposable
{
    public sealed record Marker(string Id, string Root, long Limit, int Pid, long StartTicks);
    public sealed record Cleanup(string Id, string Root, long CreatedBytes, long RemovedBytes, long RetainedBytes, string State, string? Reason);
    private const string MarkerName = "owned-test-root.json";
    private readonly Marker marker;
    public string Root => marker.Root;
    public string Evidence { get; }
    private bool disposed;
    private bool budgetReported;
    private string? retainReason;
    private readonly List<PTenoLauncher.Infrastructure.Updates.UpdateProcessIdentity> processes=[];
    public static string ProjectRoot
    {
        get { var p = new DirectoryInfo(AppContext.BaseDirectory); while (p is not null && !File.Exists(Path.Combine(p.FullName,"PTenoLauncher.sln"))) p = p.Parent; return p?.FullName ?? throw new IOException("Test source root is unavailable."); }
    }
    private static string? VerificationRoot
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("PTENO_VERIFICATION_ROOT");
            if (string.IsNullOrEmpty(root)) return null;
            var id = Path.GetFileName(root);
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "\\Averification-[0-9a-f]{32}\\z") ||
                root != SafePath.Join(ProjectRoot, ".local/test-runs/" + id) ||
                !File.Exists(SafePath.Join(root, MarkerName)))
                throw new IOException("Invalid verification operation root.");
            return root;
        }
    }
    public static string PayloadBase => VerificationRoot is { } root
        ? SafePath.Join(root, "fixtures") : SafePath.Join(ProjectRoot, ".local/test-runs");
    public static string EvidenceFor(string id) => VerificationRoot is { } root
        ? SafePath.Join(ProjectRoot, $"artifacts/verification/{Path.GetFileName(root)}/fixtures/{id}")
        : SafePath.Join(ProjectRoot, "artifacts/test-lifecycle/" + id);
    public static string BuildArtifact(string project, string assembly, string legacyRelative) => VerificationRoot is { } root
        ? SafePath.Join(root, $"bin/{project}/release/{assembly}.dll") : SafePath.Join(ProjectRoot, legacyRelative);
    public OwnedTestRoot(string purpose, long limit = 512L * 1024 * 1024)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(purpose, "\\A[a-z0-9-]{1,40}\\z")) throw new ArgumentException("Invalid test purpose.");
        var run = Environment.GetEnvironmentVariable("PTENO_TEST_RUN_ID") ?? "standalone";
        if (!System.Text.RegularExpressions.Regex.IsMatch(run, "\\A[a-zA-Z0-9-]{1,80}\\z")) throw new ArgumentException("Invalid test run ID.");
        var id = purpose + "-" + run + "-" + Guid.NewGuid().ToString("N");
        var root = SafePath.Join(PayloadBase, id);
        Evidence = EvidenceFor(id);
        Directory.CreateDirectory(root); Directory.CreateDirectory(Evidence);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        marker = new(id,root,limit,process.Id,process.StartTime.ToUniversalTime().Ticks);
        File.WriteAllText(SafePath.Join(root, MarkerName),JsonSerializer.Serialize(marker));
        File.WriteAllText(SafePath.Join(Evidence, MarkerName),JsonSerializer.Serialize(marker));
    }
    public long Measure() => Files(Root).Sum(f => new FileInfo(f).Length);
    public void CheckBudget() { if (Measure() > marker.Limit) { budgetReported=true;throw new IOException("Owned test payload budget exceeded: " + Root); } }
    public void Retain(string reason) => retainReason = reason;
    public PTenoLauncher.Infrastructure.Updates.UpdateProcessIdentity RegisterProcess(System.Diagnostics.Process process)
    {
        var executable=Path.GetFullPath(process.StartInfo.FileName);SafePath.RejectLinks(executable);
        var working=Path.GetFullPath(process.StartInfo.WorkingDirectory);
        if(working!=Root && !working.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Process was not launched in this test root.");
        var identity=new PTenoLauncher.Infrastructure.Updates.UpdateProcessIdentity(process.Id,process.StartTime.ToUniversalTime().Ticks,executable);
        RegisterIdentity(identity);return identity;
    }
    public void RegisterIdentity(PTenoLauncher.Infrastructure.Updates.UpdateProcessIdentity identity)
    {
        if(processes.Contains(identity))return;
        if(processes.Count>=32)throw new IOException("Too many owned test processes.");
        processes.Add(identity);AtomicFile.Write(SafePath.Join(Root,"owned-processes.json"),JsonSerializer.SerializeToUtf8Bytes(processes,AtomicFile.JsonOptions));
    }
    public void Preserve(string relative)
    {
        var file = SafePath.Join(Root, relative); if (!File.Exists(file)) return;
        var info = new FileInfo(file); if (info.Length > 16 * 1024 * 1024) throw new IOException("Evidence exceeds per-file limit.");
        var destination = SafePath.Join(Evidence, "records/" + relative.Replace('\\','/'));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            using var original = File.OpenRead(file); using var saved = File.OpenRead(destination);
            if (!SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(saved))) throw new IOException("Evidence cannot be overwritten.");
            return;
        }
        File.Copy(file,destination,false);
    }
    public void PreserveRunnerEvidence()
    {
        long total = 0;
        foreach (var file in Files(Root))
        {
            var relative = Path.GetRelativePath(Root,file).Replace('\\','/');
            if (relative.StartsWith("source-",StringComparison.Ordinal) || relative.StartsWith("build-",StringComparison.Ordinal)) continue;
            if (file.EndsWith(".deps.json",StringComparison.Ordinal) || file.EndsWith(".runtimeconfig.json",StringComparison.Ordinal)) continue;
            if (Path.GetExtension(file) is not (".log" or ".json" or ".trx" or ".txt" or ".png" or ".pem")) continue;
            total += new FileInfo(file).Length; if (total > 64L * 1024 * 1024) throw new IOException("Runner evidence exceeds total budget.");
            Preserve(relative);
        }
    }
    private static List<string> Files(string root)
    {
        SafePath.RejectLinks(root); var result = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var path))
        {
            SafePath.RejectLinks(path);
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                SafePath.RejectLinks(entry);
                if (Directory.Exists(entry)) pending.Push(entry); else result.Add(entry);
                if (result.Count > 100000) throw new IOException("Test root file-count limit exceeded.");
            }
        }
        return result;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (retainReason is not null)
        {
            long retained;try{retained=Measure();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){retained=-1;}
            File.WriteAllText(SafePath.Join(Evidence,"retained-"+Guid.NewGuid().ToString("N")+".json"),JsonSerializer.Serialize(new Cleanup(marker.Id,Root,retained,0,retained,"RETAINED",retainReason)));
            return;
        }
        var removed=CleanupOwned(marker,Evidence);
        if(removed.CreatedBytes>marker.Limit && !budgetReported)throw new IOException("Test payload exceeded budget; disposal completed and recorded: "+Root);
    }
    public static Cleanup CleanupOwned(Marker expected, string evidence)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(expected.Id,"\\A[a-zA-Z0-9-]{1,160}\\z")) throw new IOException("Unknown test root.");
        var allowed = SafePath.Join(PayloadBase, expected.Id);
        if (expected.Root != allowed || evidence != EvidenceFor(expected.Id)) throw new IOException("Unknown test root.");
        long bytes = 0;
        try
        {
            if (expected.Pid != Environment.ProcessId)
            {
                try { using var owner = System.Diagnostics.Process.GetProcessById(expected.Pid); if (!owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == expected.StartTicks) throw new IOException("The exact test owner process is still running."); }
                catch (ArgumentException) { }
            }
            SafePath.RejectLinks(allowed);
            var processesFile=SafePath.Join(allowed,"owned-processes.json");
            if(File.Exists(processesFile))
            {
                var identities=JsonSerializer.Deserialize<List<PTenoLauncher.Infrastructure.Updates.UpdateProcessIdentity>>(File.ReadAllText(processesFile),AtomicFile.JsonOptions)??throw new IOException("Invalid test process manifest.");
                if(identities.Count>32)throw new IOException("Test process manifest exceeds limit.");
                foreach(var identity in identities)
                {
                    try{using var child=System.Diagnostics.Process.GetProcessById(identity.Pid);if(!child.HasExited && child.StartTime.ToUniversalTime().Ticks==identity.StartTicks)throw new IOException("Recorded test child is still running: "+identity.Pid+" "+identity.Executable);}
                    catch(ArgumentException){}
                }
            }
            var markerFile = SafePath.Join(allowed,MarkerName);
            var durable=JsonSerializer.Deserialize<Marker>(File.ReadAllText(SafePath.Join(evidence,MarkerName)));
            if (durable != expected) throw new IOException("Durable test ownership marker changed.");
            if(!File.Exists(markerFile))
            {
                if(Directory.EnumerateFileSystemEntries(allowed).Any())throw new IOException("Missing internal ownership marker; nonempty root retained.");
                File.WriteAllText(markerFile,JsonSerializer.Serialize(expected));
            }
            var actual = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerFile));
            if (actual != expected) throw new IOException("Test ownership marker changed.");
            var files = Files(allowed); bytes = files.Sum(f => new FileInfo(f).Length);
            // Preflight locks without changing permissions or stopping any process.
            foreach (var f in files) { using var handle = new FileStream(f,FileMode.Open,FileAccess.Read,FileShare.None); }
            foreach(var entry in Directory.EnumerateFileSystemEntries(allowed).ToArray())
            {
                if(entry==markerFile)continue;
                SafePath.RejectLinks(entry);
                if(Directory.Exists(entry))Directory.Delete(entry,true);else File.Delete(entry);
            }
            File.Delete(markerFile);
            try{Directory.Delete(allowed,false);}
            catch(IOException){if(Directory.Exists(allowed))File.WriteAllText(markerFile,JsonSerializer.Serialize(expected));throw;}
            if (Directory.Exists(allowed)) throw new IOException("Owned payload remains.");
            var result=new Cleanup(expected.Id,allowed,bytes,bytes,0,"REMOVED",bytes>expected.Limit?"BUDGET EXCEEDED":null);Record(result);return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            long retained = bytes;
            try { if (Directory.Exists(expected.Root)) retained = Files(expected.Root).Sum(f => new FileInfo(f).Length); } catch (Exception scan) when(scan is IOException or UnauthorizedAccessException) { retained = -1; }
            Record(new(expected.Id,expected.Root,bytes,retained<0?0:Math.Max(0,bytes-retained),retained,"RETAINED",e.Message));
            throw new IOException("Test cleanup failed; payload retained and recorded: " + expected.Root,e);
        }
        void Record(Cleanup value)
        {
            SafePath.RejectLinks(evidence);
            Directory.CreateDirectory(evidence);
            File.WriteAllText(SafePath.Join(evidence,"cleanup-" + Guid.NewGuid().ToString("N") + ".json"),JsonSerializer.Serialize(value));
        }
    }
}
