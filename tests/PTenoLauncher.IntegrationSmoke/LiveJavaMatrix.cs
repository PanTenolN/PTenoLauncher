using System.Diagnostics;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

internal static class LiveJavaMatrix
{
    private sealed class Counter : DelegatingHandler
    {
        public int Requests { get; private set; }
        public Counter() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests++; return base.SendAsync(request, ct); }
    }
    public static async Task RunAsync(LauncherPaths paths)
    {
        if (Directory.EnumerateFileSystemEntries(SafePath.Join(paths.Root, "instances")).Any()) throw new InvalidOperationException("Use an isolated root without Minecraft profiles/worlds.");
        if (new DriveInfo(Path.GetPathRoot(paths.Root)!).AvailableFreeSpace < 2L * 1024 * 1024 * 1024)
            throw new IOException("The isolated runtime matrix requires at least 2 GiB free space.");
        var sentinel = SafePath.Join(paths.Root, "synthetic-java-data.txt");
        const string content = "SYNTHETIC - NOT A MINECRAFT WORLD";
        if (File.Exists(sentinel) && await File.ReadAllTextAsync(sentinel) != content) throw new InvalidOperationException("Refusing to replace existing data.");
        await AtomicFile.WriteAsync(sentinel, System.Text.Encoding.UTF8.GetBytes(content));
        using var counter = new Counter(); using var http = new HttpClient(counter) { Timeout = TimeSpan.FromMinutes(6) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PTenoLauncher/0.2.0");
        var policy = new NetworkPolicy(); var downloads = new VerifiedDownloader(http, policy);
        var versions = new VersionManager(paths, downloads); var manager = new JavaRuntimeManager(paths, new(), versions, downloads, policy);
        var results = new List<object>(); var requirements = new List<object>();
        var report = SafePath.Join(paths.Root, "java-matrix-result.json");
        async Task Save(string phase, int? current = null) => await AtomicFile.WriteJsonAsync(report,
            new { check = "live-managed-java-matrix", phase, currentMajor = current, results, minecraftRequirements = requirements, requests = counter.Requests, gameLaunched = false, authenticationBypassed = false });
        await Save("starting");
        try
        {
            foreach (var major in new[] { 8, 17, 21, 25 })
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                policy.Offline = false; await Save("installing", major);
                var runtime = await manager.EnsureManagedAsync(major, ct: timeout.Token);
                if (runtime.Major != major || !runtime.Is64Bit || !await JavaRuntimeManager.VerifyManagedPathAsync(paths, runtime.Path, timeout.Token))
                    throw new InvalidDataException("Installed Java validation failed.");
                if (major == 17)
                {
                    // Process-local fixture only: never change user/system environment settings.
                    var previous = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS");
                    try
                    {
                        Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", "-XX:PTenoSyntheticInvalidOption");
                        if ((await new JavaManager().InspectAsync(runtime.Path, timeout.Token)).Major != major) throw new InvalidDataException("Inherited Java options affected the version probe.");
                    }
                    finally { Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", previous); }
                }
                var start = new ProcessStartInfo(runtime.Path); start.ArgumentList.Add("-version");
                var exit = await LiveProcessRunner.RunAsync(start, SafePath.Join(paths.Root, $"java-{major}-{Guid.NewGuid():N}.log"), ct: timeout.Token);
                if (exit != 0) throw new InvalidDataException("Java -version failed.");
                await Save("offline-reuse", major); policy.Offline = true; int before = counter.Requests;
                var offline = await manager.EnsureManagedAsync(major, ct: timeout.Token);
                if (offline.Path != runtime.Path || counter.Requests != before) throw new InvalidDataException("Offline reuse used network or a different runtime.");
                bool repaired = false;
                if (major == 21)
                {
                    var directory = Path.GetDirectoryName(runtime.Path)!;
                    while (!File.Exists(Path.Combine(directory, "runtime.json"))) directory = Path.GetDirectoryName(directory) ?? throw new InvalidDataException("Missing runtime receipt.");
                    var receipt = (await AtomicFile.ReadJsonAsync<ManagedJavaReceipt>(Path.Combine(directory, "runtime.json"), timeout.Token))!;
                    var probe = receipt.Files.First(f => f.RelativePath != receipt.JavaRelativePath && f.Size is > 0 and < 8192);
                    var file = SafePath.Join(directory, probe.RelativePath); var original = await File.ReadAllBytesAsync(file, timeout.Token);
                    try
                    {
                        await AtomicFile.WriteAsync(file, [1, 2, 3], timeout.Token);
                        if (await JavaRuntimeManager.VerifyManagedPathAsync(paths, runtime.Path, timeout.Token)) throw new InvalidDataException("Runtime corruption was not detected.");
                        await Save("offline-repair", major);
                        var replacement = await manager.EnsureManagedAsync(major, ct: timeout.Token);
                        if (replacement.Path == runtime.Path || counter.Requests != before || !await JavaRuntimeManager.VerifyManagedPathAsync(paths, replacement.Path, timeout.Token))
                            throw new InvalidDataException("Offline cached-archive repair failed.");
                        repaired = true;
                    }
                    finally { await AtomicFile.WriteAsync(file, original); }
                }
                if (await File.ReadAllTextAsync(sentinel, timeout.Token) != content) throw new InvalidDataException("Synthetic data changed.");
                results.Add(new { major, status = "PASS", x64 = true, versionExit = exit, offlineRequests = counter.Requests - before, offlineReuse = true, corruptionRepairFromCache = repaired, syntheticDataPreserved = true });
                Console.WriteLine($"Java {major}: PASS, offline HTTP requests = 0"); await Save("runtime-complete", major);
            }
            policy.Offline = false; await Save("minecraft-metadata");
            var catalog = await versions.RefreshAsync();
            foreach (var version in new[] { "1.12.2", "1.20.1", "1.21.1", catalog.LatestRelease }.Distinct())
            {
                var profile = new GameProfile(Guid.NewGuid(), "Java metadata probe", version, ModLoader.Vanilla, 1024, null, DateTimeOffset.UtcNow);
                var required = await manager.RequiredMajorAsync(profile); int before = counter.Requests;
                policy.Offline = true; var cached = await manager.RequiredMajorAsync(profile); policy.Offline = false;
                if (cached != required || counter.Requests != before) throw new InvalidDataException("Minecraft Java metadata was not reusable offline.");
                requirements.Add(new { minecraft = version, java = required, offlineMetadata = true });
            }
            await Save("PASS"); Console.WriteLine(await File.ReadAllTextAsync(report));
        }
        catch (Exception ex)
        {
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "java-matrix-error.json"), new { error = ex.GetType().Name, message = ex.Message, completed = results.Count, gameLaunched = false });
            await Save("FAILED"); throw;
        }
    }
}
