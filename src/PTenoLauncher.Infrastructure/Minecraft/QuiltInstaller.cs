using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record QuiltLoaderVersion(string Version, bool IsPrerelease, int MinimumJava)
{
    public override string ToString()=>Version+(IsPrerelease?" (попередня)":"");
}
/// <summary>Official Quilt metadata + verified inherited Vanilla files. Does not execute an installer JAR.</summary>
public sealed class QuiltInstaller(LauncherPaths paths,VerifiedDownloader downloads,NetworkPolicy network,VanillaInstaller vanilla,ILauncherStore store)
{
    private sealed record Metadata(DateTimeOffset At,string Sha256,byte[] Body);
    public bool UsedCachedMetadata {get;private set;}
    private static string Segment(string value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>128||value is "." or ".."||!value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_' or '+'))
            throw new InvalidDataException("Некоректна версія Quilt/Minecraft.");
        return Uri.EscapeDataString(value);
    }
    private async Task<byte[]> MetadataAsync(Uri uri,int maximum,CancellationToken ct)
    {
        TrustedSources.Validate(uri,DownloadSource.Quilt);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        var path=SafePath.Join(paths.Cache,"quilt/metadata/"+key+".json");Metadata? cached=null;
        try{cached=await AtomicFile.ReadJsonAsync<Metadata>(path,ct);}catch(Exception ex)when(ex is JsonException or InvalidDataException){}
        if(cached is not null && (cached.Body is null || cached.Body.Length>maximum || Convert.ToHexString(SHA256.HashData(cached.Body))!=cached.Sha256))cached=null;
        if(network.Offline || cached is not null && DateTimeOffset.UtcNow-cached.At<TimeSpan.FromMinutes(15))
        {
            if(cached is null)throw new InvalidDataException("Немає кешованих метаданих Quilt. Потрібен інтернет для підготовки.");
            UsedCachedMetadata=true;return cached.Body;
        }
        byte[] body;
        try{body=await downloads.GetBytesAsync(uri,maximum,ct,DownloadSource.Quilt);}
        catch(Exception ex)when(!ct.IsCancellationRequested && cached is not null && ex is HttpRequestException or TaskCanceledException)
        {UsedCachedMetadata=true;return cached.Body;}
        await AtomicFile.WriteJsonAsync(path,new Metadata(DateTimeOffset.UtcNow,Convert.ToHexString(SHA256.HashData(body)),body),ct);
        return body;
    }
    private static Version NumericVersion(string text)=>System.Version.TryParse(text.Split('-','+')[0],out var v)?v:new(0,0);
    public async Task<IReadOnlyList<QuiltLoaderVersion>> GetVersionsAsync(string minecraft,CancellationToken ct=default)
    {
        UsedCachedMetadata=false;
        using var doc=JsonDocument.Parse(await MetadataAsync(new("https://meta.quiltmc.org/v3/versions/loader/"+Segment(minecraft)),8*1024*1024,ct));
        if(doc.RootElement.ValueKind!=JsonValueKind.Array || doc.RootElement.GetArrayLength()>4096)throw new InvalidDataException("Некоректний каталог Quilt.");
        var result=new List<QuiltLoaderVersion>();
        foreach(var item in doc.RootElement.EnumerateArray())
        {
            var version=item.GetProperty("loader").GetProperty("version").GetString()!;_=Segment(version);
            if(!System.Version.TryParse(version.Split('-','+')[0],out _))throw new InvalidDataException("Непідтримуваний формат версії Quilt.");
            int java=item.TryGetProperty("launcherMeta",out var meta)&&meta.TryGetProperty("min_java_version",out var minimum)?minimum.GetInt32():8;
            if(java is <8 or >100)throw new InvalidDataException("Некоректна вимога Java для Quilt.");
            result.Add(new(version,version.Contains('-'),java));
        }
        // API ordering is not chronological and does not expose a stable flag.
        return result.DistinctBy(v=>v.Version).OrderBy(v=>v.IsPrerelease).ThenByDescending(v=>NumericVersion(v.Version)).ThenByDescending(v=>v.Version,StringComparer.Ordinal).ToArray();
    }
    public async Task<InstallationReceipt> InstallAsync(GameProfile profile,string? loaderVersion=null,IProgress<DownloadProgress>? progress=null,CancellationToken ct=default)
    {
        ProfileRules.Validate(profile);if(profile.Loader!=ModLoader.Quilt)throw new InvalidOperationException("Потрібен профіль Quilt.");
        using var lease=new ProfileUsage(paths).Acquire(profile.Id);new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        InstallationReceipt? previous=null;
        try{previous=await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id),ct);if(previous is not null)ReceiptValidator.Validate(paths,previous);}
        catch(Exception ex)when(ex is JsonException or InvalidDataException){previous=null;}
        loaderVersion??=previous is {Loader:ModLoader.Quilt}?previous.LoaderVersion:null;
        try
        {
            var basis=await vanilla.InstallUnderLeaseAsync(profile with{Loader=ModLoader.Vanilla},progress,ct);
            return await PrepareUnderLeaseAsync(profile,basis,loaderVersion,progress,ct);
        }
        catch{if(previous is not null)await AtomicFile.WriteJsonAsync(paths.Receipt(profile.Id),previous,CancellationToken.None);throw;}
    }
    public async Task<InstallationReceipt> PrepareAsync(GameProfile profile,InstallationReceipt verifiedVanilla,string? loaderVersion=null,IProgress<DownloadProgress>? progress=null,CancellationToken ct=default)
    {
        ProfileRules.Validate(profile);using var lease=new ProfileUsage(paths).Acquire(profile.Id);new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        return await PrepareUnderLeaseAsync(profile,verifiedVanilla,loaderVersion,progress,ct);
    }
    private async Task<InstallationReceipt> PrepareUnderLeaseAsync(GameProfile profile,InstallationReceipt basis,string? version,IProgress<DownloadProgress>? progress,CancellationToken ct)
    {
        if(profile.Loader!=ModLoader.Quilt||basis.Loader!=ModLoader.Vanilla||basis.Version!=profile.MinecraftVersion)throw new InvalidDataException("Quilt потребує відповідної базової Vanilla-інсталяції.");
        ReceiptValidator.Validate(paths,basis);
        foreach(var file in basis.Files)
            if(!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache,file.RelativePath),file.Hash,file.Algorithm,file.Size,ct))throw new InvalidDataException("Базові файли Minecraft пошкоджені. Відновіть інсталяцію.");
        var versions=await GetVersionsAsync(profile.MinecraftVersion,ct);
        var selected=version is null?versions.FirstOrDefault(v=>!v.IsPrerelease):versions.FirstOrDefault(v=>v.Version==version);
        if(selected is null)throw new InvalidDataException("Сумісну релізну версію Quilt не знайдено; попередню версію потрібно вибрати явно.");
        using var doc=JsonDocument.Parse(await MetadataAsync(new("https://meta.quiltmc.org/v3/versions/loader/"+Segment(profile.MinecraftVersion)+"/"+Segment(selected.Version)+"/profile/json"),2*1024*1024,ct));
        var root=doc.RootElement;var main=root.GetProperty("mainClass").GetString();
        if(root.GetProperty("inheritsFrom").GetString()!=profile.MinecraftVersion || main!="org.quiltmc.loader.impl.launch.knot.KnotClient")throw new InvalidDataException("Непідтримуваний успадкований профіль Quilt.");
        var libraries=root.GetProperty("libraries").EnumerateArray().ToArray();
        if(libraries.Length is 0 or >64)throw new InvalidDataException("Некоректний список бібліотек Quilt.");
        var files=basis.Files.ToDictionary(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase);var classpath=new List<string>();
        int java=Math.Max(basis.JavaMajor,selected.MinimumJava);bool loaderFound=false;
        foreach(var lib in libraries)
        {
            var coordinate=lib.GetProperty("name").GetString()!;var relative=FabricInstaller.MavenPath(coordinate);
            var origin=new Uri(lib.GetProperty("url").GetString()!);TrustedSources.Validate(origin,DownloadSource.Quilt);
            if(origin.Query.Length!=0||origin.Fragment.Length!=0||!(origin.Host=="maven.fabricmc.net"&&origin.AbsolutePath=="/" || origin.Host=="maven.quiltmc.org"&&origin.AbsolutePath=="/repository/release/"))
                throw new InvalidDataException("Бібліотека Quilt має надходити з офіційного Maven Quilt/Fabric.");
            bool loader=coordinate.StartsWith("org.quiltmc:quilt-loader:",StringComparison.Ordinal);
            if(loader && (coordinate!="org.quiltmc:quilt-loader:"+selected.Version || loaderFound))throw new InvalidDataException("JAR Quilt не відповідає вибраній версії.");
            loaderFound|=loader;
            var url=new Uri(origin,relative);var (hash,algorithm)=await LibraryHashAsync(lib,url,ct);
            var cacheRelative="libraries/quilt/"+relative;
            if(classpath.Contains(cacheRelative,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Дублікат бібліотеки Quilt.");
            var target=SafePath.Join(paths.Cache,cacheRelative);long size;
            if(lib.TryGetProperty("size",out var length))
            {
                size=length.GetInt64();if(size is <=0 or >32*1024*1024)throw new InvalidDataException("Завелика бібліотека Quilt.");
                await downloads.DownloadAsync(url,target,hash,size,algorithm,progress,ct,DownloadSource.Quilt);
            }
            else
            {
                size=File.Exists(target)?new FileInfo(target).Length:0;
                if(size<=0||size>32*1024*1024||!await VerifiedDownloader.VerifyAsync(target,hash,algorithm,size,ct))
                {
                    var bytes=await downloads.GetBytesAsync(url,32*1024*1024,ct,DownloadSource.Quilt);
                    var actual=algorithm=="SHA512"?SHA512.HashData(bytes):SHA256.HashData(bytes);
                    if(!Convert.ToHexString(actual).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Бібліотека Quilt не пройшла перевірку хешу.");
                    size=bytes.Length;await AtomicFile.WriteAsync(target,bytes,ct);
                }
                progress?.Report(new("Quilt "+relative,size,size));
            }
            java=Math.Max(java,await InspectJarAsync(target,loader?main:null,ct));
            files.Add(cacheRelative,new(cacheRelative,hash,algorithm,size));classpath.Add(cacheRelative);
        }
        if(!loaderFound)throw new InvalidDataException("Відсутній JAR Quilt Loader.");
        var receipt=basis with{Loader=ModLoader.Quilt,LoaderVersion=selected.Version,MainClass=main!,JavaMajor=java,InstalledAt=DateTimeOffset.UtcNow,
            Files=files.Values.ToArray(),Classpath=classpath.Concat(basis.Classpath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            JvmArguments=basis.JvmArguments.Concat(Arguments(root,"jvm")).ToArray(),GameArguments=basis.GameArguments.Concat(Arguments(root,"game")).ToArray()};
        ReceiptValidator.Validate(paths,receipt);await AtomicFile.WriteJsonAsync(paths.Receipt(profile.Id),receipt,ct);
        await store.AddHistoryAsync(profile.Id,"quilt.installed",selected.Version,CancellationToken.None);return receipt;
    }
    private async Task<(string Hash,string Algorithm)> LibraryHashAsync(JsonElement lib,Uri url,CancellationToken ct)
    {
        string hash;string algorithm;
        if(lib.TryGetProperty("sha512",out var value)){hash=value.GetString()!;algorithm="SHA512";}
        else if(lib.TryGetProperty("sha256",out value)){hash=value.GetString()!;algorithm="SHA256";}
        else
        {
            algorithm="SHA512";byte[] bytes;
            try{bytes=await MetadataAsync(new(url.AbsoluteUri+".sha512"),1024,ct);}
            catch(Exception ex)when(ex is HttpRequestException {StatusCode:HttpStatusCode.NotFound} || network.Offline && ex is InvalidDataException)
            {algorithm="SHA256";bytes=await MetadataAsync(new(url.AbsoluteUri+".sha256"),1024,ct);}
            hash=Encoding.ASCII.GetString(bytes).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
        }
        if(hash.Length!=(algorithm=="SHA512"?128:64)||!hash.All(Uri.IsHexDigit))throw new InvalidDataException("Некоректна контрольна сума Quilt.");
        return(hash,algorithm);
    }
    private async Task<int> InspectJarAsync(string path,string? main,CancellationToken ct)
    {
        using var jar=ZipFile.OpenRead(path);SafeArchive.Validate(jar,paths.Cache,maxExpanded:256L*1024*1024,maxEntries:20000);
        if(main is not null && jar.GetEntry(main.Replace('.','/')+".class") is null)throw new InvalidDataException("У JAR Quilt не знайдено головний клас.");
        int java=8;
        foreach(var entry in jar.Entries.Where(e=>e.FullName.EndsWith(".class",StringComparison.Ordinal)&&e.FullName!="module-info.class"&&!e.FullName.StartsWith("META-INF/versions/",StringComparison.Ordinal)))
        {
            ct.ThrowIfCancellationRequested();await using var input=entry.Open();var header=new byte[8];
            int read=await input.ReadAtLeastAsync(header,8,throwOnEndOfStream:false,ct);
            int minor=(header[4]<<8)|header[5],major=(header[6]<<8)|header[7];
            if(read!=8||header[0]!=0xCA||header[1]!=0xFE||header[2]!=0xBA||header[3]!=0xBE||minor==65535||major is <45 or >144)
                throw new InvalidDataException("Непідтримуваний Java bytecode у бібліотеці Quilt.");
            java=Math.Max(java,major-44);
        }
        return java;
    }
    private static IReadOnlyList<string> Arguments(JsonElement root,string kind)
    {
        if(!root.TryGetProperty("arguments",out var args)||!args.TryGetProperty(kind,out var group))return [];
        if(group.ValueKind!=JsonValueKind.Array||group.GetArrayLength()>128)throw new InvalidDataException("Некоректні аргументи Quilt.");
        return group.EnumerateArray().Select(a=>a.ValueKind==JsonValueKind.String&&a.GetString()!.Length<=4096&&!a.GetString()!.Contains('\0')?a.GetString()!:throw new InvalidDataException("Непідтримуваний аргумент Quilt.")).ToArray();
    }
}
