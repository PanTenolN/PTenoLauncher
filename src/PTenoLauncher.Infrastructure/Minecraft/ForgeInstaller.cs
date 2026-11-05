using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

internal sealed record ForgeOperation(int Schema, Guid ProfileId, string OperationId, ModLoader Loader, string Version,
    string State, string? PreviousReceiptHash, InstallationReceipt? Candidate);

/// <summary>Transactional installation of the explicitly supported spec-1 client processor subset.
/// Staging limits accidental writes; it does not confine arbitrary Java code.</summary>
public sealed class ForgeInstaller
{
    private readonly LauncherPaths paths;
    private readonly ForgeComponentPreparer components;
    private readonly VanillaInstaller vanilla;
    private readonly ILauncherStore store;
    private readonly IForgeProcessorHost host;
    public ForgeInstaller(LauncherPaths paths, ForgeComponentPreparer components, VanillaInstaller vanilla,
        JavaRuntimeManager runtimes, JavaManager java, ILauncherStore store)
        : this(paths, components, vanilla, store, new ForgeProcessorHost(paths, runtimes, java)) { }
    internal ForgeInstaller(LauncherPaths paths, ForgeComponentPreparer components, VanillaInstaller vanilla,
        ILauncherStore store, IForgeProcessorHost host)
    { this.paths = paths; this.components = components; this.vanilla = vanilla; this.store = store; this.host = host; }

    public async Task<InstallationReceipt> InstallAsync(GameProfile profile, string version,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        _ = await Modpacks.MrpackRequirements.RequestedAsync(paths, profile, version, ct);
        ProfileRules.Validate(profile); _ = ForgeMaven.Source(profile.Loader); ForgeMaven.Segment(version);
        using var lease = new ProfileUsage(paths).Acquire(profile.Id); new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        var recovered = await RecoverAsync(profile, version, ct);
        if (recovered is not null) return recovered;
        var prepared = await components.PrepareUnderLeaseAsync(profile, version, progress, ct);
        // Reject unsupported execution metadata before downloading Vanilla or acquiring Java.
        // Full plan validation follows staging of verified executable inputs.
        if (prepared.Plan.ClientProcessors.Count == 0 || prepared.Plan.ClientProcessors.Any(p => p.Outputs.Count == 0))
            throw new NotSupportedException("This installer needs an adapter for processors without explicit output checksums. Component preparation remains available.");
        _ = ForgeProcessorPlanning.Build(prepared.Plan, SafePath.Join(paths.Cache, "forge/preflight/" + Guid.NewGuid().ToString("N")),
            prepared.Plan.Libraries.Where(l => !l.Generated).ToDictionary(l => l.Coordinate,
                l => SafePath.Join(paths.Cache, $"forge/libraries/sha1/{l.Sha1}/{l.MavenPath}"), StringComparer.Ordinal));
        var basis = await vanilla.InstallUnderLeaseAsync(profile with { Loader = ModLoader.Vanilla, PreferredLoaderVersion = null }, progress, ct, persist: false);
        return await CompleteUnderLeaseAsync(profile, prepared, basis, ct);
    }

    internal async Task<InstallationReceipt> CompleteUnderLeaseAsync(GameProfile profile, ForgePreparedComponents prepared,
        InstallationReceipt basis, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); ProfileRules.Validate(profile);
        if (prepared.ProfileId != profile.Id || prepared.Plan.Loader != profile.Loader || basis.Loader != ModLoader.Vanilla ||
            basis.Version != profile.MinecraftVersion || basis.LegacyAssets is not null || basis.Forge is not null)
            throw new NotSupportedException("Supported Forge installations require a matching modern Vanilla base.");
        ReceiptValidator.Validate(paths, basis); await VerifyFilesAsync(basis.Files, ct);
        var installer = prepared.Installer;
        var installerFile = new InstalledFile(installer.RelativePath, installer.Hash, installer.Algorithm, installer.Size);
        await VerifyFilesAsync([installerFile], ct);
        var plan = await ForgeInstallerMetadata.InspectAsync(SafePath.Join(paths.Cache, installer.RelativePath), paths.Cache,
            profile.Loader, prepared.Plan.LoaderVersion, profile.MinecraftVersion, ct);
        if (plan.MainClass is not ("cpw.mods.bootstraplauncher.BootstrapLauncher" or "cpw.mods.modlauncher.Launcher"))
            throw new NotSupportedException("Unsupported Forge launch entry point.");
        if (plan.MinimumJava is { } required && required > basis.JavaMajor)
            throw new NotSupportedException("Loader requires a different Java major than the verified Minecraft base; a separate adapter is required.");
        var id = Guid.NewGuid().ToString("N"); var prefix = "forge/installations/" + id + "/";
        var stage = SafePath.Join(paths.Cache, prefix.TrimEnd('/')); Directory.CreateDirectory(stage);
        var journal = new ForgeOperation(1, profile.Id, id, profile.Loader, plan.LoaderVersion, "Building", await ReceiptHashAsync(profile.Id, ct), null);
        await SaveJournalAsync(profile.Id, journal, ct);
        try
        {
            var inputs = new List<InstalledFile>();
            async Task StageInput(InstalledFile source, string relative)
            {
                await VerifyFilesAsync([source], ct);
                await CopyNewAsync(SafePath.Join(paths.Cache, source.RelativePath), SafePath.Join(stage, relative), ct);
                inputs.Add(new(prefix + relative, source.Hash, source.Algorithm, source.Size));
            }
            await StageInput(installerFile, "installer.jar");
            foreach (var library in plan.Libraries.Where(l => !l.Generated))
                await StageInput(new($"forge/libraries/sha1/{library.Sha1}/{library.MavenPath}", library.Sha1!, "SHA1", library.Size), "libraries/" + library.MavenPath);
            // VanillaInstaller appends the exact Mojang client JAR last. No filename/version heuristic.
            var client = basis.Files.Single(f => f.RelativePath == basis.Classpath[^1]);
            await StageInput(client, "minecraft.jar");
            using (var zip = ZipFile.OpenRead(SafePath.Join(stage, "installer.jar")))
            {
                long dataBytes = 0;
                foreach (var raw in plan.ClientData.Values.Where(v => !v.StartsWith('[') && !v.StartsWith('\'')).Distinct(StringComparer.Ordinal))
                {
                    var name = raw.StartsWith('/') ? raw[1..] : raw;
                    var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Embedded client data is missing.");
                    if ((dataBytes += entry.Length) > 128L * 1024 * 1024) throw new InvalidDataException("Embedded processor data exceeds 128 MiB.");
                    var bytes = await ForgeInstallerMetadata.ReadEntryAsync(entry, 128L * 1024 * 1024, ct);
                    var relative = "data/" + name;
                    var target = SafePath.Join(stage, relative);
                    if (File.Exists(target)) throw new InvalidDataException("Colliding embedded processor data.");
                    await AtomicFile.WriteAsync(target, bytes, ct);
                    inputs.Add(new(prefix + relative, Convert.ToHexString(SHA256.HashData(bytes)), "SHA256", bytes.Length));
                }
            }
            CheckTree(stage);
            var execution = ForgeProcessorPlanning.Build(plan, stage);
            var generated = new List<InstalledFile>(); bool javaVerified = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(20));
            foreach (var step in execution.Steps)
            {
                var token = deadline.Token; token.ThrowIfCancellationRequested();
                await VerifyFilesAsync(inputs.Concat(generated), token);
                foreach (var output in step.Outputs)
                {
                    var cache = GeneratedCache(output);
                    if (!File.Exists(output.Path) && await ValidOutputAsync(SafePath.Join(paths.Cache, cache), output.Sha1, token))
                        await CopyNewAsync(SafePath.Join(paths.Cache, cache), output.Path, token);
                }
                if (!await OutputsValidAsync(step, token))
                {
                    foreach (var output in step.Outputs)
                        if (File.Exists(output.Path) && !await ValidOutputAsync(output.Path, output.Sha1, token))
                            throw new InvalidDataException("Unexpected processor output; retry uses a fresh operation.");
                    if (!javaVerified) { await host.VerifyRuntimeAsync(basis.JavaMajor, token); javaVerified = true; }
                    foreach (var output in step.Outputs) Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!);
                    await host.ExecuteAsync(step, stage, SafePath.Join(stage, $"logs/processor-{generated.Count:D3}-{Guid.NewGuid():N}.log"), token);
                    CheckTree(stage);
                    await VerifyFilesAsync(inputs.Concat(generated), token);
                    if (!await OutputsValidAsync(step, token)) throw new InvalidDataException("Processor output missing, oversized or checksum mismatch.");
                }
                foreach (var output in step.Outputs)
                {
                    if (output.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    { using var zip = ZipFile.OpenRead(output.Path); SafeArchive.Validate(zip, stage, 512L * 1024 * 1024, 30000); }
                    var size = new FileInfo(output.Path).Length;
                    if (plan.Libraries.SingleOrDefault(l => l.Coordinate == output.Coordinate) is { Size: > 0 } declared && declared.Size != size)
                        throw new InvalidDataException("Generated artifact size differs from declared size.");
                    var file = new InstalledFile(prefix + Path.GetRelativePath(stage, output.Path).Replace('\\', '/'), output.Sha1, "SHA1", size);
                    generated.Add(file);
                    var cache = SafePath.Join(paths.Cache, GeneratedCache(output));
                    if (!await ValidOutputAsync(cache, output.Sha1, token)) await AtomicFile.WriteAsync(cache, await File.ReadAllBytesAsync(output.Path, token), token);
                }
                await SaveJournalAsync(profile.Id, journal, token); // durable boundary; retry re-verifies hash-addressed outputs
            }
            CheckTree(stage);
            var runtime = plan.Libraries.Where(l => l.Runtime).Select(l => prefix + "libraries/" + l.MavenPath).ToArray();
            bool foundMain = false;
            foreach (var relative in runtime.Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
            {
                using var zip = ZipFile.OpenRead(SafePath.Join(paths.Cache, relative)); SafeArchive.Validate(zip, stage, 512L * 1024 * 1024, 30000);
                foundMain |= zip.GetEntry(plan.MainClass.Replace('.', '/') + ".class") is not null;
            }
            if (!foundMain) throw new InvalidDataException("Loader runtime does not contain its declared main class.");
            var libraryRoot = "${library_directory}/" + prefix + "libraries";
            string Rewrite(string a) => a.Replace("${library_directory}", libraryRoot, StringComparison.Ordinal);
            var candidate = basis with {
                Loader = profile.Loader, LoaderVersion = plan.LoaderVersion, MainClass = plan.MainClass, InstalledAt = DateTimeOffset.UtcNow,
                Files = basis.Files.Append(installerFile).Concat(inputs).Concat(generated).DistinctBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
                Classpath = runtime.Concat(basis.Classpath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                JvmArguments = basis.JvmArguments.Concat(plan.JvmArguments.Select(Rewrite)).ToArray(),
                GameArguments = MergeGameArguments(basis.GameArguments, plan.GameArguments.Select(Rewrite).ToArray()),
                Forge = new(1, plan.VersionId, plan.FmlCoordinate, id, installerFile, generated)
            };
            ReceiptValidator.Validate(paths, candidate); await VerifyFilesAsync(candidate.Files, ct);
            journal = journal with { State = "Ready", Candidate = candidate };
            await SaveJournalAsync(profile.Id, journal, ct);
            await CommitAsync(profile, journal, ct);
            await store.AddHistoryAsync(profile.Id, "forge.installed", profile.Loader + " " + plan.LoaderVersion, CancellationToken.None);
            return candidate;
        }
        catch
        {
            // Ready is intentionally retained if interruption happened at the commit boundary.
            if (journal.State != "Ready") await SaveJournalAsync(profile.Id, journal with { State = "Interrupted" }, CancellationToken.None);
            throw;
        }
    }
    internal async Task<InstallationReceipt?> RecoverAsync(GameProfile profile, string version, CancellationToken ct)
    {
        var journal = await AtomicFile.ReadJsonAsync<ForgeOperation>(JournalPath(profile.Id), ct);
        if (journal is null) return null;
        ValidateJournal(profile.Id, journal);
        var active = await AtomicFile.ReadJsonAsync<ActiveGameRecord>(SafePath.Join(paths.Cache, "forge/installations/" + journal.OperationId + "/active-processor.json"), ct);
        if (active is not null && ActiveGameRecord.IsRunning(active))
            throw new InvalidOperationException("The previous Forge processor is still running. Wait for it to finish before recovery.");
        if (journal.State != "Ready" || journal.Loader != profile.Loader || journal.Version != version || journal.Candidate?.Version != profile.MinecraftVersion) return null;
        // An interrupted Ready candidate may have lost files. Keep the current receipt and rebuild in a new operation.
        ReceiptValidator.Validate(paths, journal.Candidate!);
        try { await VerifyFilesAsync(journal.Candidate!.Files, ct); }
        catch (InvalidDataException)
        { await SaveJournalAsync(profile.Id, journal with { State = "Interrupted", Candidate = null }, ct); return null; }
        await CommitAsync(profile, journal, ct); return journal.Candidate;
    }
    private async Task CommitAsync(GameProfile profile, ForgeOperation journal, CancellationToken ct)
    {
        ValidateJournal(profile.Id, journal);
        var candidate = journal.Candidate ?? throw new InvalidDataException("Missing transactional installation candidate.");
        ReceiptValidator.Validate(paths, candidate); await VerifyFilesAsync(candidate.Files, ct);
        var current = await ReceiptHashAsync(profile.Id, ct);
        var candidateHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(candidate, AtomicFile.JsonOptions)));
        if (current != journal.PreviousReceiptHash && current != candidateHash) throw new InvalidDataException("Installation receipt changed after this operation; recovery refused to overwrite it.");
        if (current != candidateHash)
        {
            if (current is not null)
                await CopyNewAsync(paths.Receipt(profile.Id), SafePath.Join(paths.Profile(profile.Id), "installation-backups/" + journal.OperationId + ".json"), ct);
            await AtomicFile.WriteJsonAsync(paths.Receipt(profile.Id), candidate, ct);
        }
        await SaveJournalAsync(profile.Id, journal with { State = "Committed" }, CancellationToken.None);
    }
    private static IReadOnlyList<string> MergeGameArguments(IReadOnlyList<string> basis, IReadOnlyList<string> loader)
    {
        var result = basis.ToList();
        // Modern official profiles add distinct flag/value pairs. Overrides and positional forms need their own adapter.
        if (loader.Count % 2 != 0) throw new NotSupportedException("Unsupported loader game argument shape.");
        for (int i = 0; i < loader.Count; i += 2)
        {
            if (!loader[i].StartsWith("--", StringComparison.Ordinal) || result.Contains(loader[i], StringComparer.Ordinal))
                throw new NotSupportedException("Duplicate/unsupported loader game arguments.");
            result.Add(loader[i]); result.Add(loader[i + 1]);
        }
        return result;
    }
    private static void ValidateJournal(Guid id, ForgeOperation j)
    {
        if (j.Schema != 1 || j.ProfileId != id || !Guid.TryParseExact(j.OperationId, "N", out _) || j.State is not ("Building" or "Ready" or "Committed" or "Interrupted") ||
            j.Loader is not (ModLoader.Forge or ModLoader.NeoForge) || j.PreviousReceiptHash is { } h && (h.Length != 64 || !h.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid Forge transaction journal.");
        if (j.Candidate is { } c && (c.Loader != j.Loader || c.LoaderVersion != j.Version || c.Forge?.OperationId != j.OperationId))
            throw new InvalidDataException("Forge journal candidate identity mismatch.");
    }
    private string JournalPath(Guid id) => SafePath.Join(paths.Profile(id), "forge-operation.json");
    private async Task SaveJournalAsync(Guid id, ForgeOperation j, CancellationToken ct)
    {
        ValidateJournal(id, j);
        // Keep provenance/ownership for superseded and interrupted operation trees, including local logs.
        await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Cache, "forge/installations/" + j.OperationId + "/operation.json"), j, ct);
        await AtomicFile.WriteJsonAsync(JournalPath(id), j, ct);
    }
    private async Task<string?> ReceiptHashAsync(Guid id, CancellationToken ct)
    {
        var file = paths.Receipt(id); if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException("Existing installation receipt exceeds 32 MiB.");
        await using var stream = File.OpenRead(file); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    private async Task VerifyFilesAsync(IEnumerable<InstalledFile> files, CancellationToken ct)
    { foreach (var f in files) if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, f.RelativePath), f.Hash, f.Algorithm, f.Size, ct)) throw new InvalidDataException("Installation input changed or is missing: " + f.RelativePath); }
    private static string GeneratedCache(ForgeOutput o) => "forge/generated/sha1/" + o.Sha1.ToLowerInvariant() + "/" + ForgeCoordinate.Parse(o.Coordinate).Path;
    private static async Task<bool> OutputsValidAsync(ForgeProcessorStep step, CancellationToken ct)
    { foreach (var o in step.Outputs) if (!await ValidOutputAsync(o.Path, o.Sha1, ct)) return false; return true; }
    private static Task<bool> ValidOutputAsync(string file, string hash, CancellationToken ct)
    { SafePath.RejectLinks(file); var size = File.Exists(file) ? new FileInfo(file).Length : 0; return size is > 0 and <= 256L * 1024 * 1024 ? VerifiedDownloader.VerifyAsync(file, hash, "SHA1", size, ct) : Task.FromResult(false); }
    private static async Task CopyNewAsync(string source, string destination, CancellationToken ct)
    {
        SafePath.RejectLinks(source); SafePath.RejectLinks(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // A retry at the pre-receipt backup boundary must accept only identical existing bytes.
        if (File.Exists(destination))
        {
            await using var sourceStream = File.OpenRead(source); await using var destinationStream = File.OpenRead(destination);
            var sourceHash = await SHA256.HashDataAsync(sourceStream, ct);
            var destinationHash = await SHA256.HashDataAsync(destinationStream, ct);
            if (new FileInfo(source).Length != new FileInfo(destination).Length ||
                !sourceHash.SequenceEqual(destinationHash))
                throw new IOException("Refusing to overwrite an existing operation file.");
            return;
        }
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, ct); await output.FlushAsync(ct); output.Flush(true);
    }
    private static void CheckTree(string root)
    {
        int count = 0; long size = 0;
        void Walk(string dir)
        {
            SafePath.RejectLinks(dir);
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                SafePath.RejectLinks(entry);
                if (++count > 50000) throw new InvalidDataException("Processor operation contains too many entries.");
                if (Directory.Exists(entry)) Walk(entry);
                else if ((size += new FileInfo(entry).Length) > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Processor operation exceeds 4 GiB.");
            }
        }
        Walk(root);
    }
}
