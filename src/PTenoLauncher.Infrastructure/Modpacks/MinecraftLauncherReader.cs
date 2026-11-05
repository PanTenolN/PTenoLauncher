using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed record MinecraftLauncherChoice(string Key, string Name, string Version) { public override string ToString() => Name + " · " + Version; }
public sealed record MinecraftLauncherRequest(string ProfileKey = "", string ProfileName = "", string? ContentDirectory = null, bool PinMods = false);
public sealed record MinecraftLauncherPreview(string ProfilesFile, MinecraftLauncherRequest Request, IReadOnlyList<MinecraftLauncherChoice> Choices,
    InstancePackPreview Snapshot, string RequiredContentDirectory)
{ public MrpackPreview Plan => Snapshot.Plan; }

// Strict data-only subset of official launcher profile JSON, never a launcher/accounts migration.
public sealed class MinecraftLauncherReader(LauncherPaths paths, ILauncherStore store)
{
    private static readonly string[] ProfileKeys = ["name", "lastVersionId", "gameDir", "type", "created", "lastUsed", "icon", "javaArgs", "javaDir", "resolution", "logConfig", "logConfigIsXML"];
    public async Task<MinecraftLauncherPreview> PreviewAsync(string profilesFile, MinecraftLauncherRequest request, CancellationToken ct = default)
    {
        try { return await Read(profilesFile, request, ct); }
        catch (Exception e) when (e is not MrpackException && e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        { throw new MrpackException("MinecraftImportMetadata"); } // Never retain an exception body containing foreign profile/account data.
    }
    private async Task<MinecraftLauncherPreview> Read(string file, MinecraftLauncherRequest request, CancellationToken ct)
    {
        file = Path.GetFullPath(file); if(file.StartsWith("\\\\",StringComparison.Ordinal))throw new MrpackException("MinecraftImportContent");SafePath.RejectLinks(file);
        if (Path.GetFileName(file) is not ("launcher_profiles.json" or "launcher_profiles_microsoft_store.json")) throw new MrpackException("MinecraftImportMetadata");
        var bytes = await AtomicFile.ReadBytesAsync(file, 4 * 1024 * 1024, ct); var hash = MrpackReader.Hash(bytes);
        using var doc = MrpackReader.Json(bytes); var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object || profiles.EnumerateObject().Count() > 256) throw new MrpackException("MinecraftImportMetadata");
        // Only sanitized profile choices leave this reader. Other root fields (including old auth databases) are never copied/logged.
        var choices = new List<MinecraftLauncherChoice>();
        foreach (var item in profiles.EnumerateObject())
        {
            if (item.Name.Length is < 1 or > 128 || item.Name.Any(char.IsControl) || item.Value.ValueKind != JsonValueKind.Object) throw new MrpackException("MinecraftImportMetadata");
            choices.Add(new(item.Name, Text(item.Value,"name",false,80) ?? item.Name, Text(item.Value,"lastVersionId",false) ?? ""));
        }
        var blockers = new List<MrpackProblem>(); var components = new List<InstanceComponent>();
        string minecraft = "", loaderVersion = "", name = "Minecraft Launcher", required = ""; var loader = ModLoader.Vanilla;
        string metadataHash = hash;
        if (!profiles.TryGetProperty(request.ProfileKey, out var profile)) blockers.Add(new("MinecraftImportChoose", ""));
        else
        {
            name = string.IsNullOrWhiteSpace(request.ProfileName) ? Text(profile,"name",false,80) ?? request.ProfileKey : request.ProfileName.Trim();
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) blockers.Add(new("ZipRequirements","profile"));
            if (profile.EnumerateObject().Any(p => !ProfileKeys.Contains(p.Name,StringComparer.Ordinal))) blockers.Add(new("MinecraftImportUnsupported","profile fields"));
            var requested = Text(profile,"lastVersionId",true)!;
            if (!ReferenceId(requested) || requested is "latest" or "latest-release" or "latest-snapshot") blockers.Add(new("MinecraftImportUnsupported","exact lastVersionId"));
            else
            {
                var installation = Path.GetDirectoryName(file)!; var versionFile = SafePath.Join(installation,$"versions/{requested}/{requested}.json");
                if (!File.Exists(versionFile)) blockers.Add(new("MinecraftImportUnsupported","version metadata missing"));
                else
                {
                    var versionBytes = await AtomicFile.ReadBytesAsync(versionFile,MrpackReader.MaxIndexBytes,ct); using var version = MrpackReader.Json(versionBytes); var v = version.RootElement;
                    if (Text(v,"id",true) != requested) throw new MrpackException("MinecraftImportMetadata");
                    var parent = Text(v,"inheritsFrom",false); var main = Text(v,"mainClass",true)!;
                    var loaderId = main switch { "net.fabricmc.loader.impl.launch.knot.KnotClient" or "net.fabricmc.loader.launch.knot.KnotClient" => "net.fabricmc.fabric-loader", "org.quiltmc.loader.impl.launch.knot.KnotClient" => "org.quiltmc.quilt-loader", _ => null };
                    byte[]? baseBytes = null;
                    if (parent is null)
                    {
                        if (!InstancePackReader.Exact(requested) || main != "net.minecraft.client.main.Main" || !Vanilla(v)) blockers.Add(new("MinecraftImportUnsupported","vanilla metadata"));
                        else minecraft = requested;
                    }
                    else if (!InstancePackReader.Exact(parent) || loaderId is null) blockers.Add(new("MinecraftImportUnsupported","inherited/custom loader metadata"));
                    else
                    {
                        var parentFile = SafePath.Join(installation,$"versions/{parent}/{parent}.json");
                        if (!File.Exists(parentFile)) blockers.Add(new("MinecraftImportUnsupported","base metadata missing"));
                        else
                        {
                            baseBytes = await AtomicFile.ReadBytesAsync(parentFile,MrpackReader.MaxIndexBytes,ct); using var basis = MrpackReader.Json(baseBytes); var b = basis.RootElement;
                            if (Text(b,"id",true) != parent || Text(b,"inheritsFrom",false) is not null || Text(b,"mainClass",true) != "net.minecraft.client.main.Main" || !Vanilla(b)) blockers.Add(new("MinecraftImportUnsupported","base metadata"));
                            else minecraft = parent;
                        }
                        var coordinatePrefix = loaderId == "net.fabricmc.fabric-loader" ? "net.fabricmc:fabric-loader:" : "org.quiltmc:quilt-loader:";
                        if (!v.TryGetProperty("libraries",out var libraries) || libraries.ValueKind != JsonValueKind.Array || libraries.GetArrayLength() > 128) throw new MrpackException("MinecraftImportMetadata");
                        var coordinates = libraries.EnumerateArray().Select(l => Text(l,"name",true)!).ToArray();
                        if(coordinates.Distinct(StringComparer.Ordinal).Count()!=coordinates.Length || coordinates.Any(c=>!SupportedLibrary(c)))blockers.Add(new("MinecraftImportUnsupported","additional/duplicate libraries"));
                        if(coordinates.Any(c=>c.StartsWith("net.fabricmc:intermediary:",StringComparison.Ordinal)&&c!="net.fabricmc:intermediary:"+parent))blockers.Add(new("MinecraftImportUnsupported","intermediary target"));
                        var matching = coordinates.Where(c => c.StartsWith(coordinatePrefix,StringComparison.Ordinal)).ToArray();
                        if (matching.Length != 1 || !InstancePackReader.Exact(matching[0][coordinatePrefix.Length..])) blockers.Add(new("MinecraftImportUnsupported","loader coordinate"));
                        else { loaderVersion = matching[0][coordinatePrefix.Length..]; loader = loaderId == "net.fabricmc.fabric-loader" ? ModLoader.Fabric : ModLoader.Quilt; }
                        if (v.EnumerateObject().Any(p => p.Name is not ("id" or "inheritsFrom" or "mainClass" or "libraries" or "arguments" or "type" or "time" or "releaseTime" or "minimumLauncherVersion"))) blockers.Add(new("MinecraftImportUnsupported","custom version fields"));
                        if (v.TryGetProperty("arguments",out var args) && (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Any(p => p.Name is not ("game" or "jvm") || p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() != 0))) blockers.Add(new("MinecraftImportUnsupported","custom launch arguments"));
                    }
                    if (minecraft.Length > 0) components.Add(new("net.minecraft",minecraft));
                    if (loader != ModLoader.Vanilla) components.Add(new(loaderId!,loaderVersion));
                    metadataHash = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { profiles = hash, version = MrpackReader.Hash(versionBytes), parent = baseBytes is null ? null : MrpackReader.Hash(baseBytes) }));
                }
            }
            var declared = Text(profile,"gameDir",false,1024);
            if (declared is null) required = Path.GetDirectoryName(file)!;
            else if (!Path.IsPathFullyQualified(declared)) blockers.Add(new("MinecraftImportUnsupported","relative gameDir"));
            else required = Path.GetFullPath(declared);
        }
        var content = new List<InstanceContentFile>(); var entries = new List<MrpackFilePreview>(); var excluded = new List<string>();
        var layout = new List<InstanceSourceEntry>(); string directory = request.ContentDirectory is null ? "" : Path.GetFullPath(request.ContentDirectory);
        if (directory.Length == 0 || required.Length == 0 || !directory.Equals(required,StringComparison.OrdinalIgnoreCase)) blockers.Add(new("MinecraftImportContent",required));
        else
        {
            if (directory.StartsWith("\\\\",StringComparison.Ordinal)||!Directory.Exists(directory) || directory.Equals(paths.Root,StringComparison.OrdinalIgnoreCase) || paths.Root.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new MrpackException("MinecraftImportContent");
            foreach(var managed in new[]{Path.Combine(paths.Root,"instances"),Path.Combine(paths.Root,"modpack-imports"),paths.Cache})
                if(managed.Equals(directory,StringComparison.OrdinalIgnoreCase)||managed.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new MrpackException("MinecraftImportContent");
            using var source = new InstanceSource(directory,clientContent:true); layout.AddRange(source.Entries);
            foreach (var e in source.Entries.Where(e => !e.Directory))
            {
                bool selected = ZipPackReader.Eligible(e.Path);
                if (selected) content.Add(new(e.Path,e.Path,e.Size,await source.Hash(e.Path,ct))); else excluded.Add(e.Path);
                entries.Add(new(e.Path,"required","unsupported",selected,false,true,e.Size,selected?e.Path.Split('/')[0]:"excluded"));
            }
            foreach(var e in source.Entries.Where(e=>e.Directory && e.Path.Split('/').Length==1 && e.Path is not ("mods" or "config" or "resourcepacks" or "shaderpacks"))) entries.Add(new(e.Path,"unsupported","unsupported",false,false,true,0,"excluded-directory"));
        }
        if (content.Count > MrpackReader.MaxFiles || content.Count(f=>MrpackReader.RootJar(f.Path)) > 128) throw new MrpackException("PackBudget");
        if (content.Count == 0) blockers.Add(new("ZipContent",""));
        ModCartReview? review = null;
        if (blockers.Count == 0) review = await new InstancePackReader(paths,store).ReviewSourceAsync(directory,content,name,minecraft,loader,ct);
        if(review is not null) blockers.AddRange(review.Blockers.Select(b=>new MrpackProblem("PackCompatibility",b)));
        var sourceHash = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { metadataHash,layout,content },AtomicFile.JsonOptions));
        var fingerprint = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { file,request,directory,sourceHash },AtomicFile.JsonOptions));
        var plan = new MrpackPreview(directory,content.Sum(c=>c.Size),sourceHash,name,"launcher-profile-json","",name,minecraft,loader,loader==ModLoader.Vanilla?null:loaderVersion,request.PinMods,[],
            content.Select(c=>new MrpackFile(c.Path,c.Size,c.SourcePath,null,c.Path.Split('/')[0])).ToArray(),entries,blockers,fingerprint) { SourceKind="local-minecraft-launcher",ContentRoot="content",Excluded=excluded };
        return new(file,request,choices,new(new(name,request.PinMods),plan,content,components,["InstanceExclusions","InstanceDefaults"],review,metadataHash),required);
    }
    private static bool ReferenceId(string s)=>s.Length is >0 and <=100&&s is not "." and not ".."&&s.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_');
    private static string? Text(JsonElement e,string name,bool required,int max=128)
    {
        if(e.ValueKind!=JsonValueKind.Object)throw new MrpackException("MinecraftImportMetadata");
        if(!e.TryGetProperty(name,out var v)){if(required)throw new MrpackException("MinecraftImportMetadata");return null;}
        if(v.ValueKind!=JsonValueKind.String || v.GetString() is not {} s || s.Length>max || s.Any(char.IsControl) || required&&s.Length==0)throw new MrpackException("MinecraftImportMetadata");return s.Length==0?null:s;
    }
    private static bool Vanilla(JsonElement v)
    {
        if(!v.TryGetProperty("downloads",out var d)||d.ValueKind!=JsonValueKind.Object||!d.TryGetProperty("client",out var c)||c.ValueKind!=JsonValueKind.Object)return false;
        var hash=Text(c,"sha1",true);var url=Text(c,"url",true,2048);
        return hash is {Length:40}&&hash.All(Uri.IsHexDigit)&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&uri.Port==443&&uri.Host is "piston-data.mojang.com" or "launcher.mojang.com";
    }
    private static bool SupportedLibrary(string coordinate)
    {
        var p=coordinate.Split(':');if(p.Length!=3||!InstancePackReader.Exact(p[2]))return false;
        return p[0] switch {
            "org.ow2.asm"=>p[1] is "asm" or "asm-analysis" or "asm-commons" or "asm-tree" or "asm-util",
            "net.fabricmc"=>p[1] is "fabric-loader" or "intermediary" or "sponge-mixin" or "tiny-mappings-parser" or "mapping-io" or "access-widener" or "tiny-remapper",
            "org.quiltmc"=>p[1] is "quilt-loader" or "hashed" or "quilt-config", _=>false };
    }
}
