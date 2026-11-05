using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;

// Local packaging only. Never creates keys, uploads releases, changes trust or performs Authenticode signing.
try
{
    if (args.Length == 0) throw new ArgumentException("Commands: inventory <publish>; package <publish> <zip>; index <package-json> <changes-file> <channel> <output>; sign <index> <external-private-pem> <key-id> <source-root> <sequence-ledger> <envelope>");
    if (args[0] == "inventory" && args.Length == 2)
    {
        var root = Path.GetFullPath(args[1]); var build = ReadBuild(SafePath.Join(root, "PTenoLauncher.Infrastructure.dll"));
        LauncherUpdateTrust.ValidateBuild(build); var files = new List<LauncherUpdateFile>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative is LauncherUpdater.InstallationManifest or "portable.flag") continue;
            if (relative.StartsWith(".pteno", StringComparison.Ordinal)) throw new InvalidDataException("Cannot inventory an active or previously used installation.");
            SafePath.RejectLinks(file); files.Add(new(relative, new FileInfo(file).Length, await Hash(file)));
        }
        // Validate all managed path/size/hash rules through the same public package contract.
        var validation = new LauncherUpdatePackage("Portable", new("https://github.com/PanTenolN/PTenoLauncher/releases/download/validation/p.zip"), 1, new string('0', 64), files);
        LauncherUpdateTrust.ValidatePackage(validation);
        if (!files.Any(f => f.Path == "updater/PTenoLauncher.UpdateHelper.exe")) throw new InvalidDataException("Publish the separate helper into updater/ before inventory generation.");
        await AtomicFile.WriteJsonAsync(SafePath.Join(root, LauncherUpdater.InstallationManifest), new ManagedLauncherInstallation(build, files));
        Console.WriteLine(JsonSerializer.Serialize(build)); return 0;
    }
    if (args[0] == "package" && args.Length is 4 or 7)
    {
        var root = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]); var url = new Uri(args[3]); LauncherUpdateTrust.ValidateOrigin(url);
        var inventory = await AtomicFile.ReadJsonAsync<ManagedLauncherInstallation>(SafePath.Join(root, LauncherUpdater.InstallationManifest)) ?? throw new InvalidDataException("Missing inventory.");
        if (ReadBuild(SafePath.Join(root, "PTenoLauncher.Infrastructure.dll")) != inventory.Build) throw new InvalidDataException("Build identity changed.");
        foreach (var f in inventory.Files)
            if (!await PTenoLauncher.Infrastructure.Network.VerifiedDownloader.VerifyAsync(SafePath.Join(root, f.Path), f.Sha256, "SHA256", f.Size)) throw new InvalidDataException("Changed payload: " + f.Path);
        using (var stream = new FileStream(output, FileMode.CreateNew))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            foreach (var f in inventory.Files) zip.CreateEntryFromFile(SafePath.Join(root, f.Path), f.Path, CompressionLevel.Optimal);
        var p = new LauncherUpdatePackage(args.Length == 7 ? "Installed" : "Portable", url, new FileInfo(output).Length, await Hash(output), inventory.Files,
            args.Length == 7 ? new Uri(args[5]) : null, args.Length == 7 ? new FileInfo(args[4]).Length : 0,
            args.Length == 7 ? await Hash(args[4]) : null, args.Length == 7 ? "inno-single-process-v1" : null);
        if (args.Length == 7 && args[6] != "inno-single-process-v1") throw new InvalidDataException("Unsupported installer contract.");
        LauncherUpdateTrust.ValidatePackage(p);
        await AtomicFile.WriteJsonAsync(output + ".json", new LauncherRelease(inventory.Build, "", [p])); return 0;
    }
    if (args[0] == "index" && args.Length is 5 or 6)
    {
        var release = await AtomicFile.ReadJsonAsync<LauncherRelease>(args[1]) ?? throw new InvalidDataException("Missing package descriptor.");
        if (release.Build.Channel != args[3]) throw new InvalidDataException("Channel does not match the compiled build.");
        release = release with { Changes = await File.ReadAllTextAsync(args[2]) };
        var releases = args.Length == 6 ? (await AtomicFile.ReadJsonAsync<LauncherUpdateIndex>(args[5]) ?? throw new InvalidDataException("Invalid previous index.")).Releases.ToList() : [];
        var previous = releases.SingleOrDefault(r => r.Build.Sequence == release.Build.Sequence);
        if (previous is not null)
        {
            if (previous.Build != release.Build) throw new InvalidDataException("Sequence/build collision.");
            release = release with { Packages = previous.Packages.Concat(release.Packages).DistinctBy(p => p.Mode).ToArray() }; releases.Remove(previous);
        }
        releases.Add(release);
        var index = new LauncherUpdateIndex(1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), releases.OrderByDescending(r => r.Build.Sequence).Take(64).ToArray());
        await AtomicFile.WriteJsonAsync(Path.GetFullPath(args[4]), index); return 0;
    }
    if (args[0] == "sign" && args.Length == 7)
    {
        var keyFile = Path.GetFullPath(args[2]); var sourceRoot = Path.GetFullPath(args[4]).TrimEnd(Path.DirectorySeparatorChar);
        if (keyFile.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) || keyFile.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Private signing keys must remain outside the repository/source tree.");
        var payload = await File.ReadAllBytesAsync(args[1]); var index = JsonSerializer.Deserialize<LauncherUpdateIndex>(payload, AtomicFile.JsonOptions) ?? throw new InvalidDataException("Invalid index.");
        using var key = ECDsa.Create(); key.ImportFromPem(await File.ReadAllTextAsync(keyFile));
        if (key.KeySize != 256) throw new InvalidDataException("Expected P-256 key.");
        var envelope = new SignedLauncherUpdates(args[3], payload, key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        _ = new LauncherUpdateTrust(new Dictionary<string, string> { [args[3]] = key.ExportSubjectPublicKeyInfoPem() }).Verify(envelope, DateTimeOffset.UtcNow);
        var ledger = Path.GetFullPath(args[5]); SafePath.RejectLinks(ledger); Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        using var gate = new FileStream(ledger, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(gate, leaveOpen: true); var prior = await reader.ReadToEndAsync();
        var sequence = index.Releases.Max(r => r.Build.Sequence);
        if (prior.Length > 0 && !long.TryParse(prior, out _)) throw new InvalidDataException("Corrupt sequence ledger.");
        if (long.TryParse(prior, out var last) && sequence <= last) throw new InvalidDataException("Release sequence must exceed the ledger across both channels.");
        var bytes = System.Text.Encoding.ASCII.GetBytes(sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)); gate.Position = 0; await gate.WriteAsync(bytes); gate.SetLength(bytes.Length); gate.Flush(true);
        await AtomicFile.WriteJsonAsync(Path.GetFullPath(args[6]), envelope);
        Console.WriteLine("Signed metadata only. No upload or Authenticode signing. Configure reviewed production public trust separately."); return 0;
    }
    throw new ArgumentException("Invalid arguments. Packaging tools never configure production trust automatically.");
}
catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1; }

static async Task<string> Hash(string file) { SafePath.RejectLinks(file); await using var stream = File.OpenRead(file); return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant(); }
static LauncherBuild ReadBuild(string file)
{
    // Metadata-only inspection; do not load or execute the input assembly.
    using var stream = File.OpenRead(file); using var pe = new PEReader(stream); var reader = pe.GetMetadataReader();
    if (reader.ManifestResources.Any(h => reader.GetString(reader.GetManifestResource(h).Name) == "PTenoLauncher.UpdaterTestPublicKey")) throw new InvalidDataException("Test-trust binaries are not production packages.");
    var values = new Dictionary<string, string>();
    foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
    {
        var a = reader.GetCustomAttribute(handle); if (a.Constructor.Kind != HandleKind.MemberReference) continue;
        var m = reader.GetMemberReference((MemberReferenceHandle)a.Constructor); if (m.Parent.Kind != HandleKind.TypeReference) continue;
        var t = reader.GetTypeReference((TypeReferenceHandle)m.Parent);
        if (reader.GetString(t.Namespace) != "System.Reflection" || reader.GetString(t.Name) != "AssemblyMetadataAttribute") continue;
        var blob = reader.GetBlobReader(a.Value); if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid attribute.");
        var key = blob.ReadSerializedString(); var value = blob.ReadSerializedString(); if (key is not null && value is not null) values.Add(key, value);
    }
    return new(long.Parse(values["PTenoBuildSequence"], System.Globalization.CultureInfo.InvariantCulture), values["PTenoBuildId"], reader.GetAssemblyDefinition().Version.ToString(3), values["PTenoBuildChannel"]);
}
