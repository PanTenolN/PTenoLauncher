using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

// Opt-in live network checks, never run by the unit test suite; no authentication or game execution.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".local/integration");
var paths = new LauncherPaths(root); paths.Initialize();
var denyNetwork = args.Length > 1 && (args[1] is "--mods-offline" or "--local-deps-offline" or "--optional-offline" or "--quilt-offline") ? new DenyNetworkHandler() : null;
using var http = denyNetwork is null ? NetworkPolicy.CreateHttpClient() : new HttpClient(denyNetwork);
var network = new NetworkPolicy(); var downloader = new VerifiedDownloader(http, network);
var versions = new VersionManager(paths, downloader);
if (args.Length > 1 && args[1] == "--java-matrix")
{
    await PTenoLauncher.IntegrationSmoke.LiveJavaMatrix.RunAsync(paths); return;
}
if (args.Length > 2 && args[1] == "--java-process-check")
{
    // Explicit opt-in probe of an existing runtime; no account, game or network request.
    network.Offline = true;
    var runtime = await new JavaManager().InspectAsync(Path.GetFullPath(args[2]));
    if (!runtime.Is64Bit || !JavaRuntimeManager.IsX64Executable(runtime.Path)) throw new InvalidDataException("Expected x64 Java.");
    var executable = new System.Diagnostics.ProcessStartInfo(runtime.Path);
    executable.ArgumentList.Add("-version");
    var code = await PTenoLauncher.Infrastructure.Diagnostics.LiveProcessRunner.RunAsync(executable,
        SafePath.Join(paths.Root, "java-version-" + Guid.NewGuid().ToString("N") + ".log"));
    var invalid = new System.Diagnostics.ProcessStartInfo(runtime.Path);
    invalid.ArgumentList.Add("-XX:PTenoSyntheticInvalidOption");
    var failed = await PTenoLauncher.Infrastructure.Diagnostics.LiveProcessRunner.RunAsync(invalid,
        SafePath.Join(paths.Root, "java-invalid-option-" + Guid.NewGuid().ToString("N") + ".log"));
    if (code != 0 || failed == 0) throw new InvalidDataException("Unexpected Java exit codes.");
    var result = new { check = "real-java-process", java = runtime.Major, x64 = true, versionExit = code,
        invalidArgumentExit = failed, networkRequests = 0, gameLaunched = false, authenticationBypassed = false };
    await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "java-process-result.json"), result);
    Console.WriteLine(JsonSerializer.Serialize(result, AtomicFile.JsonOptions)); return;
}
if (args.Length > 1 && args[1] == "--quilt-mod-inspection")
{
    // One real public mod artifact in an explicitly isolated directory; no account/game execution.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync(timeout.Token);
    var quiltModCatalog = new PTenoLauncher.Infrastructure.Mods.ModrinthClient(paths, downloader, network);
    var project = await quiltModCatalog.GetProjectAsync("ok-zoomer", timeout.Token);
    var candidates = await quiltModCatalog.GetVersionsAsync(project.Id, "1.20.1", ModLoader.Quilt, timeout.Token);
    var chosen = candidates.First(); var artifact = chosen.File;
    var file = SafePath.Join(paths.Cache, "modrinth/files/" + artifact.Sha512 + "/" + artifact.Name);
    await downloader.DownloadAsync(artifact.Url, file, artifact.Sha512, artifact.Size, "SHA512", ct: timeout.Token, source: DownloadSource.Modrinth);
    var inspector = new PTenoLauncher.Infrastructure.Mods.LocalModManager(paths, store);
    var inspected = await inspector.InspectAsync(file, timeout.Token);
    if (inspected.Loader != ModLoader.Quilt || inspected.QuiltMetadata is null || inspected.Id is null)
        throw new Exception("Expected real native Quilt metadata.");
    var profile = new GameProfile(Guid.NewGuid(), "Quilt metadata isolated check", "1.20.1", ModLoader.Quilt, 4096, null, DateTimeOffset.UtcNow);
    await store.SaveProfileAsync(profile, timeout.Token);
    var sentinel = SafePath.Join(paths.Game(profile.Id), "saves/synthetic-metadata-check.txt");
    await File.WriteAllTextAsync(sentinel, "SYNTHETIC - NOT A MINECRAFT WORLD", timeout.Token);
    network.Offline = true;
    var repeat = await inspector.InspectAsync(file, timeout.Token);
    var review = await inspector.ReviewAsync(profile, [new(file, inspected)], timeout.Token);
    if (repeat.Sha256 != inspected.Sha256 || review.Blockers.Count == 0 ||
        Directory.GetFiles(SafePath.Join(paths.Game(profile.Id), "mods")).Length != 0 ||
        await File.ReadAllTextAsync(sentinel, timeout.Token) != "SYNTHETIC - NOT A MINECRAFT WORLD")
        throw new Exception("Expected preserved isolated profile and unresolved native Quilt dependencies.");
    var result = new { check = "real-quilt-mod-static-inspection", project = project.Id, version = chosen.Id,
        minecraft = "1.20.1", modId = inspected.Id, modVersion = inspected.Version, artifact = artifact.Name,
        sha512Verified = true, metadataFromCache = quiltModCatalog.UsedCache, parsedDependencies = inspected.QuiltMetadata.Dependencies.Count,
        blockers = review.Blockers, metadataWarnings = inspected.MetadataWarnings, offlineRepeatPreservedHash = true,
        syntheticSavePreserved = true, installedMods = 0, gameLaunched = false };
    await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "quilt-mod-inspection-result.json"), result, timeout.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, AtomicFile.JsonOptions)); return;
}
if (args.Length > 1 && args[1] == "--repair-offline")
{
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    var p = (await store.GetProfilesAsync()).Last(x => x.Name == "Integration smoke");
    var receipt = (await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(p.Id)))!;
    ReceiptValidator.Validate(paths, receipt);
    var probe = receipt.Files.First(x => x.Size is > 32 and < 4096 && x.RelativePath.StartsWith("assets/objects/"));
    var file = SafePath.Join(paths.Cache, probe.RelativePath);
    if (!await VerifiedDownloader.VerifyAsync(file, probe.Hash, probe.Algorithm, probe.Size)) throw new Exception("Probe was already invalid.");
    var original = await File.ReadAllBytesAsync(file);
    try
    {
        await AtomicFile.WriteAsync(file, [0, 1, 2]);
        if (await VerifiedDownloader.VerifyAsync(file, probe.Hash, probe.Algorithm, probe.Size)) throw new Exception("Tampering not detected.");
        var installer = new VanillaInstaller(paths, versions, downloader, store);
        await installer.InstallAsync(p);
        if (!await VerifiedDownloader.VerifyAsync(file, probe.Hash, probe.Algorithm, probe.Size)) throw new Exception("Repair failed.");
        network.Offline = true;
        var offline = await installer.InstallAsync(p);
        Console.WriteLine(JsonSerializer.Serialize(new { version = p.MinecraftVersion, repairedRealArtifact = true, offlineReinstall = true, verifiedFiles = offline.Files.Count, gameLaunched = false }, AtomicFile.JsonOptions));
    }
    finally
    {
        if (!await VerifiedDownloader.VerifyAsync(file, probe.Hash, probe.Algorithm, probe.Size)) await AtomicFile.WriteAsync(file, original);
    }
    return;
}
if (args.Length > 1 && args[1] == "--java")
{
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    var profile = (await store.GetProfilesAsync()).First(p => p.MinecraftVersion == "1.21.1");
    var runtimes = new JavaRuntimeManager(paths, new JavaManager(), versions, downloader, network);
    var runtime = await runtimes.EnsureAsync(profile);
    await store.SaveProfileAsync(profile with { JavaPath = runtime.Path });
    network.Offline = true;
    var cachedRuntime = await runtimes.EnsureAsync(profile with { JavaPath = runtime.Path });
    Console.WriteLine(JsonSerializer.Serialize(new { java = runtime.Major, x64 = runtime.Is64Bit, path = runtime.Path, offlineReuse = cachedRuntime.Path == runtime.Path, gameLaunched = false }, AtomicFile.JsonOptions));
    return;
}
if(args.Length>1 && args[1]=="--quilt-repair-online")
{
    var repairStore=new SqliteLauncherStore(paths);await repairStore.InitializeAsync();
    GameProfile? repairProfile=null;InstallationReceipt? repairReceipt=null;
    foreach(var candidate in (await repairStore.GetProfilesAsync()).Where(p=>p.Loader==ModLoader.Quilt))
    {
        var candidateReceipt=await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(candidate.Id));
        if(candidateReceipt is {Loader:ModLoader.Quilt}){repairProfile=candidate;repairReceipt=candidateReceipt;break;}
    }
    if(repairProfile is null||repairReceipt is null)throw new Exception("Prepare private Quilt fixture first.");
    var vanillaProfile=(await repairStore.GetProfilesAsync()).First(p=>p.Loader==ModLoader.Vanilla&&p.MinecraftVersion==repairProfile.MinecraftVersion);
    var repairBasis=(await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(vanillaProfile.Id)))!;
    var repairProbe=repairReceipt.Files.Single(f=>f.RelativePath.Contains("/quilt-loader/"));
    var repairTarget=SafePath.Join(paths.Cache,repairProbe.RelativePath);
    if(!await VerifiedDownloader.VerifyAsync(repairTarget,repairProbe.Hash,repairProbe.Algorithm,repairProbe.Size))throw new Exception("Initial loader probe invalid.");
    var repairOriginal=await File.ReadAllBytesAsync(repairTarget);var repairSentinel=Path.Combine(paths.Game(repairProfile.Id),"saves","synthetic-quilt-check.txt");var repairWorld=await File.ReadAllBytesAsync(repairSentinel);
    try
    {
        File.Delete(repairTarget);
        var repairInstaller=new QuiltInstaller(paths,downloader,network,new(paths,versions,downloader,repairStore),repairStore);
        await repairInstaller.PrepareAsync(repairProfile,repairBasis,repairReceipt.LoaderVersion);
        if(!await VerifiedDownloader.VerifyAsync(repairTarget,repairProbe.Hash,repairProbe.Algorithm,repairProbe.Size))throw new Exception("Native loader repair failed.");
        var repairedWorld=await File.ReadAllBytesAsync(repairSentinel);
        if(!repairWorld.SequenceEqual(repairedWorld))throw new Exception("Synthetic world changed.");
        Console.WriteLine(JsonSerializer.Serialize(new{check="native-dotnet-quilt-library-repair",quilt=repairReceipt.LoaderVersion,
            downloadedRealLoader=true,hashVerified=true,syntheticSavePreserved=true,gameLaunched=false},AtomicFile.JsonOptions));
    }
    finally
    {
        if(!await VerifiedDownloader.VerifyAsync(repairTarget,repairProbe.Hash,repairProbe.Algorithm,repairProbe.Size))await AtomicFile.WriteAsync(repairTarget,repairOriginal);
    }
    return;
}

if(args.Length>1 && args[1]=="--quilt-metadata")
{
    var store=new SqliteLauncherStore(paths);await store.InitializeAsync();
    var quilt=new QuiltInstaller(paths,downloader,network,new(paths,versions,downloader,store),store);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var quiltCatalog=await quilt.GetVersionsAsync("1.21.1",timeout.Token);
    if(quiltCatalog.Count==0||quilt.UsedCachedMetadata)throw new Exception("Expected a fresh official .NET metadata fetch.");
    Console.WriteLine(JsonSerializer.Serialize(new{check="fresh-dotnet-quilt-metadata",versions=quiltCatalog.Count,latestRelease=quiltCatalog.First(v=>!v.IsPrerelease).Version,gameLaunched=false},AtomicFile.JsonOptions));return;
}

if(args.Length>1 && (args[1] is "--quilt" or "--quilt-offline"))
{
    if(args[1]=="--quilt-offline")network.Offline=true;
    var store=new SqliteLauncherStore(paths);await store.InitializeAsync();
    var source=(await store.GetProfilesAsync()).First(p=>p.MinecraftVersion=="1.21.1"&&p.Loader==ModLoader.Vanilla);
    var p=source with{Id=Guid.NewGuid(),Name="Quilt official integration",Loader=ModLoader.Quilt};await store.SaveProfileAsync(p);
    var sentinel=Path.Combine(paths.Game(p.Id),"saves","synthetic-quilt-check.txt");await File.WriteAllTextAsync(sentinel,"SYNTHETIC - NOT A MINECRAFT WORLD");
    var installer=new QuiltInstaller(paths,downloader,network,new(paths,versions,downloader,store),store);
    Console.Error.WriteLine("Quilt integration: preparing verified Vanilla and official loader libraries; no game execution.");
    var progress=new Progress<DownloadProgress>(d=>{if(d.Received==d.Total)Console.Error.WriteLine("Verified: "+d.Name);});
    var receipt=await installer.InstallAsync(p,progress:progress);Console.Error.WriteLine("Quilt receipt committed: "+receipt.LoaderVersion+"; verifying all files.");
    foreach(var file in receipt.Files)
        if(!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache,file.RelativePath),file.Hash,file.Algorithm,file.Size))throw new Exception("Invalid Quilt artifact: "+file.RelativePath);
    network.Offline=true;Console.Error.WriteLine("Quilt integration: repair using installed version pin and cached verified libraries.");
    var repaired=await installer.InstallAsync(p);
    if(repaired.LoaderVersion!=receipt.LoaderVersion)throw new Exception("Quilt repair changed the installed pin.");
    if(await File.ReadAllTextAsync(sentinel)!="SYNTHETIC - NOT A MINECRAFT WORLD")throw new Exception("Synthetic save changed.");
    var auth=new PTenoLauncher.Infrastructure.Authentication.MicrosoftAuthentication(new(paths),http,network);
    var report=await new OfflineReadiness(paths,new(),auth).CheckAsync(p);
    if(report.Ready||report.Issues.Any(i=>i.Code!="account.missing")||!report.Issues.Any(i=>i.Code=="account.missing"))throw new Exception(report.Summary);
    Console.WriteLine(JsonSerializer.Serialize(new{check="official-quilt-installation",cachedOfficialFixture=args[1]=="--quilt-offline",nativeDotnetOnlineVerified=args[1]=="--quilt",networkRequests=denyNetwork?.Requests,minecraft=receipt.Version,quilt=receipt.LoaderVersion,
        files=receipt.Files.Count,classpath=receipt.Classpath.Count,java=receipt.JavaMajor,mainClass=receipt.MainClass,
        installedPinPreserved=true,syntheticSavePreserved=true,offlineRepair=true,accountMissingAsExpected=true,gameLaunched=false},AtomicFile.JsonOptions));return;
}

if (args.Length > 1 && args[1] is "--fabric" or "--fabric-mods")
{
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    var source = (await store.GetProfilesAsync()).First(p => p.MinecraftVersion == "1.21.1" && p.Loader == ModLoader.Vanilla);
    var p = source with { Id = Guid.NewGuid(), Name = "Fabric live integration", Loader = ModLoader.Fabric };
    await store.SaveProfileAsync(p);
    var sentinel = Path.Combine(paths.Game(p.Id), "saves", "synthetic-preservation-check.txt");
    await File.WriteAllTextAsync(sentinel, "SYNTHETIC - NOT A MINECRAFT WORLD");
    var fabric = new FabricInstaller(paths, downloader, network, new(paths, versions, downloader, store), store);
    Console.Error.WriteLine("Fabric live test: installing base game and loader; no game execution.");
    var progress = new Progress<DownloadProgress>(d => { if (d.Received == d.Total) Console.Error.WriteLine("Verified download: " + d.Name); });
    var result = await fabric.InstallAsync(p, progress: progress);
    Console.Error.WriteLine("Fabric receipt committed; verifying installed files.");
    foreach (var file in result.Files)
        if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath),file.Hash,file.Algorithm,file.Size)) throw new Exception("Fabric file verification failed.");
    var loaderJar = result.Classpath.First(f => f.Contains("/fabric-loader/"));
    using (var zip = System.IO.Compression.ZipFile.OpenRead(SafePath.Join(paths.Cache, loaderJar)))
        if (zip.GetEntry(result.MainClass.Replace('.', '/') + ".class") is null) throw new Exception("Fabric main class not found.");
    network.Offline = true;
    Console.Error.WriteLine("Reinstalling with network policy Offline.");
    var offline = await fabric.InstallAsync(p);
    if (offline.LoaderVersion != result.LoaderVersion || await File.ReadAllTextAsync(sentinel) != "SYNTHETIC - NOT A MINECRAFT WORLD") throw new Exception("Offline repair/preservation failed.");
    int installedMods = 0;
    if (args[1] == "--fabric-mods")
    {
        network.Offline = false;
        var api = new PTenoLauncher.Infrastructure.Mods.ModrinthClient(paths, downloader, network);
        var project = await api.GetProjectAsync("reeses-sodium-options");
        var mods = new PTenoLauncher.Infrastructure.Mods.RemoteModInstaller(paths, store, api, downloader, new(paths, store));
        var plan = await mods.PreviewAsync(p, [new(project.Id)]);
        if (!plan.Resolution.CanInstall) throw new Exception(string.Join("\n", plan.Resolution.Blockers));
        await mods.InstallAsync(p, plan, true);
        var known = await mods.PreviewAsync(p, [new(project.Id)]);
        if (!known.Resolution.CanInstall || known.Resolution.Entries.Any(e => !e.AlreadyInstalled)) throw new Exception("Installed mod identification failed.");
        network.Offline = true;
        var cachedModPlan = await mods.PreviewAsync(p, [new(project.Id)]);
        if (!cachedModPlan.Resolution.CanInstall || cachedModPlan.Resolution.Entries.Any(e => !e.AlreadyInstalled)) throw new Exception("Offline Fabric mod validation failed.");
        await mods.InstallAsync(p, cachedModPlan, true);
        installedMods = Directory.EnumerateFiles(Path.Combine(paths.Game(p.Id), "mods"), "*.jar").Count();
        Console.Error.WriteLine("Fabric + Modrinth modules verified in one profile; no game execution.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { check = "real-fabric-install-and-offline-repair", installedMods,
        minecraft = result.Version, fabric = result.LoaderVersion, mainClass = result.MainClass, classpath = result.Classpath.Count,
        verifiedFiles = result.Files.Count, java = result.JavaMajor, offlineRepair = true, syntheticSavePreserved = true, gameLaunched = false }, AtomicFile.JsonOptions));
    return;
}

if (args.Length > 2 && (args[1] is "--mods-offline" or "--local-deps-offline"))
{
    network.Offline = true;
    var sourcePaths = new LauncherPaths(args[2]);
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    var source = (await store.GetProfilesAsync()).First(p => p.MinecraftVersion == "1.21.1" && p.Loader == ModLoader.Vanilla);
    var p = source with { Id = Guid.NewGuid(), Name = "Cached Fabric + Modrinth offline readiness", Loader = ModLoader.Fabric };
    await store.SaveProfileAsync(p);
    var sentinel = Path.Combine(paths.Game(p.Id), "saves", "synthetic-preservation-check.txt");
    await File.WriteAllTextAsync(sentinel, "SYNTHETIC - NOT A MINECRAFT WORLD");
    var fabric = new FabricInstaller(paths, downloader, network, new(paths, versions, downloader, store), store);
    Console.Error.WriteLine("Offline integration: preparing Fabric entirely from the verified cache.");
    InstallationReceipt receipt;
    if(args[1]=="--local-deps-offline")
    {
        InstallationReceipt? preparedReceipt=null;
        foreach(var candidate in (await store.GetProfilesAsync()).Where(x=>x.Loader==ModLoader.Fabric && x.Id!=p.Id))
        {
            var candidateReceipt=await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(candidate.Id));
            if(candidateReceipt is {Loader:ModLoader.Fabric,LoaderVersion:not null} && candidateReceipt.Version==p.MinecraftVersion)
            {ReceiptValidator.Validate(paths,candidateReceipt);preparedReceipt=candidateReceipt;break;}
        }
        receipt=preparedReceipt??throw new InvalidOperationException("Prepare a verified Fabric fixture first; a profile label alone is not evidence of installation.");
        await AtomicFile.WriteJsonAsync(paths.Receipt(p.Id),receipt);
    }
    else receipt=await fabric.InstallAsync(p);
    Console.Error.WriteLine("Offline integration: resolving cached Modrinth metadata and copying verified real artifacts.");
    var api = new PTenoLauncher.Infrastructure.Mods.ModrinthClient(sourcePaths, downloader, network);
    var local = new PTenoLauncher.Infrastructure.Mods.LocalModManager(paths, store);
    var remote = new PTenoLauncher.Infrastructure.Mods.RemoteModInstaller(paths, store, api, downloader, local);
    var project = await api.GetProjectAsync("reeses-sodium-options");
    PTenoLauncher.Infrastructure.Mods.LocalDependencyPlan? localPlan=null;
    string? localInput=null;byte[]? localBefore=null;
    if(args[1]=="--local-deps-offline")
    {
        var available=await api.GetVersionsAsync(project.Id,p.MinecraftVersion,p.Loader);
        var selected=available.First(v=>v.Number=="mc1.21.1-2.2.4+fabric");
        localInput=SafePath.Join(sourcePaths.Cache,"modrinth/files/"+selected.File.Sha512+"/"+selected.File.Name);
        localBefore=await File.ReadAllBytesAsync(localInput);
        localPlan=await remote.PreviewLocalAsync(p,[new(localInput,await local.InspectAsync(localInput))]);
        if(!localPlan.CanInstall)throw new Exception(string.Join("\n",localPlan.Blockers.Concat(localPlan.Remote.Resolution.Blockers)));
    }
    var plan = localPlan?.Remote ?? await remote.PreviewAsync(p, [new(project.Id)]);
    if (!plan.Resolution.CanInstall) throw new Exception(string.Join("\n", plan.Resolution.Blockers));
    foreach (var planned in plan.Resolution.Entries.Where(e=>!e.FromLocalFile))
    {
        var mod = planned.Version.File;
        var relative = "modrinth/files/" + mod.Sha512 + "/" + mod.Name;
        var input = SafePath.Join(sourcePaths.Cache, relative);
        if (!await VerifiedDownloader.VerifyAsync(input, mod.Sha512, "SHA512", mod.Size)) throw new Exception("Previously downloaded real mod cache is missing or corrupt.");
        var destination = SafePath.Join(paths.Cache, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(input, destination, true);
    }
    if(localPlan is null)await remote.InstallAsync(p, plan, true);
    else await remote.InstallLocalAsync(p,localPlan,true,false);
    if(localInput is not null)
    {
        var after=await File.ReadAllBytesAsync(localInput);
        if(!localBefore!.SequenceEqual(after))throw new Exception("Original local JAR changed.");
    }
    Console.Error.WriteLine("Offline integration: checking installed mod hashes and dependencies.");
    var modReport = await new PTenoLauncher.Infrastructure.Mods.InstalledModReadiness(paths, local).CheckAsync(p, receipt);
    if (modReport.Issues.Count != 0) throw new Exception(JsonSerializer.Serialize(modReport, AtomicFile.JsonOptions));
    var auth = new PTenoLauncher.Infrastructure.Authentication.MicrosoftAuthentication(new(paths), http, network);
    Console.Error.WriteLine("Offline integration: checking complete Minecraft, Java, mod and account readiness.");
    var report = await new OfflineReadiness(paths, new(), auth, local).CheckAsync(p);
    if (!report.Issues.Any(i => i.Code == "account.missing") || report.Ready || report.Issues.Any(i => i.Code != "account.missing"))
        throw new Exception("Expected only the missing licensed Microsoft account: " + report.Summary);
    if (await File.ReadAllTextAsync(sentinel) != "SYNTHETIC - NOT A MINECRAFT WORLD") throw new Exception("Synthetic world changed.");
    Console.WriteLine(JsonSerializer.Serialize(new { check = localPlan is null ? "real-cached-fabric-modrinth-offline-readiness" : "real-local-jar-dependencies-offline", localFiles=localPlan?.LocalFiles.Count, localFileUnchanged=localPlan is not null, minecraft = receipt.Version,
        fabric = receipt.LoaderVersion, verifiedMinecraftFiles = report.VerifiedFiles, verifiedMods = report.VerifiedMods,
        cachedDependencies = plan.Resolution.Entries.Select(e => e.Title + " " + e.Version.Number), metadataFromCache = plan.CachedMetadata,
        networkRequests = denyNetwork!.Requests, syntheticSavePreserved = true, accountMissingAsExpected = true, ready = report.Ready, gameLaunched = false }, AtomicFile.JsonOptions));
    return;
}

if(args.Length>2 && args[1]=="--optional-offline")
{
    network.Offline=true;var source=new LauncherPaths(args[2]);
    var store=new SqliteLauncherStore(paths);await store.InitializeAsync();
    var p=new GameProfile(Guid.NewGuid(),"Optional real metadata check","1.21.1",ModLoader.Fabric,2048,null,DateTimeOffset.UtcNow);await store.SaveProfileAsync(p);
    var api=new PTenoLauncher.Infrastructure.Mods.ModrinthClient(source,downloader,network);
    var local=new PTenoLauncher.Infrastructure.Mods.LocalModManager(paths,store);
    var installer=new PTenoLauncher.Infrastructure.Mods.RemoteModInstaller(paths,store,api,downloader,local);
    var version=await api.GetVersionAsync("XtuPOKUN");
    var roots=new[]{new RemoteModRequest(version.ProjectId,version.Id)};
    var initial=await installer.PreviewLocalAsync(p,[],roots);
    if(!initial.CanInstall || initial.Remote.Resolution.Optional.Count==0)throw new Exception("Expected real cached optional metadata.");
    var option=initial.Remote.Resolution.Optional[0];
    var selected=await installer.PreviewLocalAsync(p,[],roots,optionalSelections:[option.Key]);
    if(selected.CanInstall || selected.Remote.Resolution.Blockers.Count==0 || !selected.Remote.Resolution.Optional.Any(o=>o.Key==option.Key))
        throw new Exception("Expected the uncached optional component to block offline and keep a recoverable choice.");
    var cleared=await installer.PreviewLocalAsync(p,[],roots);
    if(!cleared.CanInstall || cleared.Remote.Resolution.Entries.Count!=initial.Remote.Resolution.Entries.Count)throw new Exception("Clearing optional selection did not restore the base plan.");
    if(Directory.EnumerateFiles(Path.Combine(paths.Game(p.Id),"mods")).Any())throw new Exception("Preview changed profile mods.");
    Console.WriteLine(JsonSerializer.Serialize(new{check="real-optional-metadata-cold-cache",version=version.Number,
        optionalCount=initial.Remote.Resolution.Optional.Count,selectedProject=option.Dependency.ProjectId,missingOptionalBlocked=true,
        choicePreserved=true,clearedPlanValid=true,networkRequests=denyNetwork!.Requests,profileFilesChanged=false,gameLaunched=false},AtomicFile.JsonOptions));return;
}

if (args.Length > 1 && args[1] == "--modrinth")
{
    var slug = args.Length > 2 ? args[2] : "reeses-sodium-options";
    var game = args.Length > 3 ? args[3] : "1.21.1";
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    var profile = new GameProfile(Guid.NewGuid(), "Modrinth live integration", game, ModLoader.Fabric, 2048, null, DateTimeOffset.UtcNow);
    await store.SaveProfileAsync(profile);
    var api = new PTenoLauncher.Infrastructure.Mods.ModrinthClient(paths, downloader, network);
    var project = await api.GetProjectAsync(slug);
    var hits = await api.SearchAsync(project.Title, game, ModLoader.Fabric);
    if (!hits.Any(h => h.Id == project.Id)) throw new Exception("Real filtered Modrinth search did not return the expected project.");
    var remote = new PTenoLauncher.Infrastructure.Mods.RemoteModInstaller(paths, store, api, downloader, new(paths, store));
    var plan = await remote.PreviewAsync(profile, [new(project.Id)]);
    if (!plan.Resolution.CanInstall) throw new Exception(string.Join("\n", plan.Resolution.Blockers));
    await remote.InstallAsync(profile, plan, true);
    var identified = await remote.PreviewAsync(profile, [new(project.Id)]);
    if (!identified.Resolution.CanInstall || identified.Resolution.Entries.Any(e => !e.AlreadyInstalled)) throw new Exception("Installed file identity failed.");
    network.Offline = true;
    var target = profile with { Id = Guid.NewGuid(), Name = "Offline Modrinth integration" }; await store.SaveProfileAsync(target);
    var offline = await remote.PreviewAsync(target, [new(project.Id)]);
    if (!offline.Resolution.CanInstall) throw new Exception(string.Join("\n", offline.Resolution.Blockers));
    await remote.InstallAsync(target, offline, true);
    Console.WriteLine(JsonSerializer.Serialize(new {
        check = "real-modrinth-download-dependency-and-offline-install",
        project = project.Title, searchVerified = true,
        mods = plan.Resolution.Entries.Select(e => new {e.Title, e.Version.Number, e.Reason}),
        verifiedFiles = Directory.EnumerateFiles(Path.Combine(paths.Game(target.Id), "mods"), "*.jar").Count(),
        offlineInstall = true, gameLaunched = false
    }, AtomicFile.JsonOptions));
    return;
}

if (args.Length > 1 && args[1] != "--install") throw new ArgumentException("Unknown integration check: " + args[1]);
var catalog = await versions.RefreshAsync();
var entry = catalog.Versions.First(v => v.Id == catalog.LatestRelease);
using var manifest = await versions.GetVersionAsync(entry);
network.Offline = true;
var cached = await versions.GetCachedAsync();
using var cachedVersion = await versions.GetVersionAsync(entry);
if (cached?.LatestRelease != catalog.LatestRelease) throw new Exception("Offline catalog mismatch.");
Console.WriteLine(JsonSerializer.Serialize(new { check = "official-mojang-metadata", catalogVersions = catalog.Versions.Count,
    latestRelease = entry.Id, verifiedManifest = manifest.RootElement.GetProperty("id").GetString(),
    offlineCatalog = true, offlineVersion = cachedVersion.RootElement.GetProperty("id").GetString() }, AtomicFile.JsonOptions));
if (args.Length > 1 && args[1] == "--install")
{
    var version = args.Length > 2 ? args[2] : catalog.LatestRelease;
    var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    var p = new GameProfile(Guid.NewGuid(), "Integration smoke", version, ModLoader.Vanilla, 4096, null, DateTimeOffset.UtcNow);
    await store.SaveProfileAsync(p); network.Offline = false;
    var receipt = await new VanillaInstaller(paths, versions, downloader, store).InstallAsync(p);
    ReceiptValidator.Validate(paths, receipt);
    foreach (var file in receipt.Files)
        if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size))
            throw new Exception("Installed artifact verification failed.");
    Console.WriteLine($"Real installation verified: {receipt.Version}; {receipt.Files.Count} files. Game NOT launched; Microsoft login still required.");
}
sealed class DenyNetworkHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Requests++; throw new InvalidOperationException("Network request attempted during offline integration test."); }
}
