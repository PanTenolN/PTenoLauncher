using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;

public sealed class LauncherUpdateException(string code, Exception? inner = null) : InvalidOperationException(code, inner) { public string Code { get; } = code; }
public sealed record LauncherBuild(long Sequence, string Id, string Version, string Channel, int DataSchema = 2)
{
    public static LauncherBuild Current
    {
        get
        {
            var attributes = typeof(LauncherBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
            return new(long.TryParse(attributes.GetValueOrDefault("PTenoBuildSequence"), out var n) ? n : 0,
                attributes.GetValueOrDefault("PTenoBuildId") ?? "local-unversioned", typeof(LauncherBuild).Assembly.GetName().Version!.ToString(3), attributes.GetValueOrDefault("PTenoBuildChannel") ?? "preview");
        }
    }
}
public sealed record LauncherUpdateFile(string Path, long Size, string Sha256);
public sealed record LauncherUpdatePackage(string Mode, Uri Url, long Size, string Sha256, IReadOnlyList<LauncherUpdateFile> Files, Uri? InstallerUrl = null, long InstallerSize = 0, string? InstallerSha256 = null, string? InstallerContract = null);
public sealed record LauncherRelease(LauncherBuild Build, string Changes, IReadOnlyList<LauncherUpdatePackage> Packages);
public sealed record LauncherUpdateIndex(int Schema, DateTimeOffset Issued, DateTimeOffset Expires, IReadOnlyList<LauncherRelease> Releases);
public sealed record SignedLauncherUpdates(string KeyId, byte[] Payload, byte[] Signature);
public sealed record LauncherUpdateOffer(LauncherBuild Current, LauncherRelease Release, LauncherUpdatePackage Package, SignedLauncherUpdates Signed);
public sealed record LauncherUpdateCheck(string State, LauncherUpdateOffer? Offer = null);
public sealed record ManagedLauncherInstallation(LauncherBuild Build, IReadOnlyList<LauncherUpdateFile> Files, int Schema = 1);

/// <summary>Trust is compiled into the application. No keys from environment, settings, feed or package are trusted.</summary>
public sealed class LauncherUpdateTrust(IReadOnlyDictionary<string, string> publicKeys)
{
    // Intentionally empty until the owner provisions a production signing key through reviewed source/release automation.
    // Never insert a test key or private key here. Authenticode is a separate distribution concern.
    public static LauncherUpdateTrust Production { get; } = CreateCompiledTrust();
    private static LauncherUpdateTrust CreateCompiledTrust()
    {
#if PTENO_UPDATE_TEST
        using var stream = typeof(LauncherUpdateTrust).Assembly.GetManifestResourceStream("PTenoLauncher.UpdaterTestPublicKey") ?? throw new InvalidDataException("Missing isolated test trust.");
        using var reader = new StreamReader(stream);
        return new(new Dictionary<string, string> { ["isolated-test"] = reader.ReadToEnd() });
#else
        return new(new Dictionary<string, string>());
#endif
    }
    internal static readonly JsonSerializerOptions StrictJson = new(AtomicFile.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static T Parse<T>(byte[] bytes)
    {
        if (bytes.Length is 0 or > 4 * 1024 * 1024) throw new LauncherUpdateException("UpdaterInvalid");
        using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
        void Check(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new LauncherUpdateException("UpdaterInvalid"); Check(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Check(item);
        }
        Check(doc.RootElement); return JsonSerializer.Deserialize<T>(bytes, StrictJson) ?? throw new LauncherUpdateException("UpdaterInvalid");
    }
    public bool Configured => publicKeys.Count > 0;
    public LauncherUpdateIndex Verify(SignedLauncherUpdates envelope, DateTimeOffset now, bool allowExpiredForRecovery = false)
    {
        if (envelope is null || envelope.KeyId is null || !publicKeys.TryGetValue(envelope.KeyId, out var pem)) throw new LauncherUpdateException("UpdaterTrust");
        if (envelope.Payload is null || envelope.Signature is null || envelope.Payload.Length is 0 or > 1024 * 1024 || envelope.Signature.Length != 64) throw new LauncherUpdateException("UpdaterSignature");
        using var key = ECDsa.Create(); key.ImportFromPem(pem);
        if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value || !key.VerifyData(envelope.Payload, envelope.Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new LauncherUpdateException("UpdaterSignature");
        var index = Parse<LauncherUpdateIndex>(envelope.Payload);
        if (index.Schema != 1 || index.Releases is null || index.Releases.Any(r => r is null || r.Build is null) || index.Issued > now.AddMinutes(5) || !allowExpiredForRecovery && index.Expires <= now || index.Expires <= index.Issued || index.Expires - index.Issued > TimeSpan.FromDays(30) || index.Releases.Count > 64 ||
            index.Releases.Select(r => r.Build.Sequence).Distinct().Count() != index.Releases.Count || index.Releases.Select(r => r.Build.Id).Distinct(StringComparer.Ordinal).Count() != index.Releases.Count) throw new LauncherUpdateException("UpdaterInvalid");
        foreach (var r in index.Releases)
        {
            ValidateBuild(r.Build);
            if (r.Changes is null || r.Changes.Length > 16000 || r.Packages is null || r.Packages.Any(p => p is null) || r.Packages.Count is 0 or > 2 || r.Packages.Select(p => p.Mode).Distinct().Count() != r.Packages.Count) throw new LauncherUpdateException("UpdaterInvalid");
            foreach (var p in r.Packages) ValidatePackage(p);
        }
        return index;
    }
    public static void ValidateBuild(LauncherBuild b)
    {
        if (b is null || b.Sequence <= 0 || b.Id is null || b.Id.Length is 0 or > 128 || !b.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') || string.IsNullOrWhiteSpace(b.Version) || b.Version.Length > 128 || b.Version.Any(char.IsControl) || b.Channel is not ("stable" or "preview") || b.DataSchema != 2) throw new LauncherUpdateException("UpdaterBuild");
    }
    public static void ValidatePackage(LauncherUpdatePackage p)
    {
        if (p is null) throw new LauncherUpdateException("UpdaterInvalid"); ValidateOrigin(p.Url);
        if (p.Mode is not ("Portable" or "Installed") || p.Size is <= 0 or > 1024L * 1024 * 1024 || !Sha(p.Sha256)) throw new LauncherUpdateException("UpdaterInvalid");
        ValidateFiles(p.Files);
        if (p.InstallerUrl is not null)
        { ValidateOrigin(p.InstallerUrl); if (p.Mode != "Installed" || p.InstallerContract != "inno-single-process-v1" || !Sha(p.InstallerSha256) || p.InstallerSize is <= 0 or > 1024L * 1024 * 1024) throw new LauncherUpdateException("UpdaterInvalid"); }
        if (!p.Files.Any(f => f.Path == "PTenoLauncher.exe") || !p.Files.Any(f => f.Path == "PTenoLauncher.dll")) throw new LauncherUpdateException("UpdaterInvalid");
    }
    internal static void ValidateFile(LauncherUpdateFile f)
    {
        if (f is null || f.Path is null || !f.Path.IsNormalized(NormalizationForm.FormC) || !Sha(f.Sha256) || f.Size is <= 0 or > 512L * 1024 * 1024) throw new LauncherUpdateException("UpdaterInvalid");
        _ = SafePath.Join(Path.GetTempPath(), f.Path);
        if (f.Path.StartsWith(".pteno", StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterPath");
        if (f.Path.Split('/').Any(p => new[] { "portable-data", "secrets", "instances", "backups", "saves", "mods", "config", "cache", "launcher-updates" }.Contains(p, StringComparer.OrdinalIgnoreCase)) ||
            f.Path.Equals("portable.flag", StringComparison.OrdinalIgnoreCase) || f.Path.Equals("launcher-installation.json", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) || f.Path.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterPath");
    }
    internal static void ValidateFiles(IReadOnlyList<LauncherUpdateFile> files)
    {
        if (files is null || files.Count is 0 or > 4096 || files.Any(f => f is null)) throw new LauncherUpdateException("UpdaterInvalid");
        foreach (var f in files) ValidateFile(f);
        if (files.Sum(f => f.Size) > 2L * 1024 * 1024 * 1024 || files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new LauncherUpdateException("UpdaterInvalid");
        ValidatePathUnion(files.Select(f => f.Path));
    }
    internal static void ValidatePathUnion(IEnumerable<string> paths)
    {
        var files = paths.Distinct(StringComparer.Ordinal).ToArray(); var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var parts = f.Split('/'); var prefix = "";
            for (int i = 0; i < parts.Length; i++)
            {
                prefix += (i == 0 ? "" : "/") + parts[i];
                if (names.TryGetValue(prefix, out var prior) && prior != prefix) throw new LauncherUpdateException("UpdaterPath"); names[prefix] = prefix;
                if (i < parts.Length - 1 && files.Contains(prefix, StringComparer.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterPath");
            }
        }
    }
    private static bool Sha(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
    public static void ValidateOrigin(Uri uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Host != "github.com" ||
            !uri.AbsolutePath.StartsWith("/PanTenolN/PTenoLauncher/releases/download/", StringComparison.Ordinal) || uri.AbsolutePath.Contains("%", StringComparison.Ordinal)) throw new LauncherUpdateException("UpdaterOrigin");
    }
}
