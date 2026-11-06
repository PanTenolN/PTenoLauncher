using PTenoLauncher.Core;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed class LauncherPaths
{
    public string Root { get; }
    public string Database => Path.Combine(Root, "settings.db");
    public string Cache => Path.Combine(Root, "cache");
    public string Logs => Path.Combine(Root, "logs");
    public string Secrets => Path.Combine(Root, "secrets");
    public string Backups => Path.Combine(Root, "backups");
    public LauncherPaths(string root) => Root = Path.GetFullPath(root);
    public string Profile(Guid id) => FindProfileFolder(id) ?? SafePath.Join(Root, LegacyProfileRelative(id));
    public string Game(Guid id) => SafePath.Join(Profile(id), "game");
    public string Receipt(Guid id) => SafePath.Join(Profile(id), "installation.json");
    internal string ProfileRelative(Guid id) => Path.GetRelativePath(Root, Profile(id)).Replace('\\', '/');
    internal static string LegacyProfileRelative(Guid id) => $"instances/{id:N}";
    internal static string IdentityFile(Guid id) => $".pteno-profile-{id:N}";
    internal static bool IsLegacyProfileRelative(Guid id, string relative)
    {
        if (relative.Equals(LegacyProfileRelative(id), StringComparison.OrdinalIgnoreCase)) return true;
        const string prefix = "instances/";
        var suffix = "-" + id.ToString("N");
        if (!relative.StartsWith(prefix, StringComparison.Ordinal) || !relative.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        var slug = relative[prefix.Length..^suffix.Length];
        return slug.Length is > 0 and <= 48 && slug.All(c => char.IsLetterOrDigit(c) || c == '-');
    }
    internal string CompactProfilePath(GameProfile profile)
    {
        var name = ReadableName.Stem(profile.Name, "Profile", 64);
        if (Guid.TryParseExact(name, "N", out _) ||
            name.Length > 33 && name[^33] == '-' && Guid.TryParseExact(name[^32..], "N", out _))
            name += " profile";
        return ReadableName.Free(SafePath.Join(Root, "instances"), name);
    }

    internal static bool HasIdentity(string directory, Guid id)
    {
        var marker = Path.Combine(directory, IdentityFile(id));
        try { _ = File.GetAttributes(marker); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        SafePath.RejectLinks(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length != 0 ||
            Directory.EnumerateFileSystemEntries(directory, ".pteno-profile-*").Take(2).Count() != 1)
            throw new IOException("Invalid profile folder identity.");
        return true;
    }

    internal static void EnsureIdentity(string directory, Guid id)
    {
        if (HasIdentity(directory, id)) return;
        using var marker = new FileStream(SafePath.Join(directory, IdentityFile(id)), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.Flush(flushToDisk: true);
    }

    internal IDisposable AcquireProfileNames() =>
        new FileStream(SafePath.Join(Root, "profile-names.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal string PlaceProfileDirectory(GameProfile profile, string stagedDirectory)
    {
        using var names = AcquireProfileNames();
        if (FindProfileFolder(profile.Id) is not null) throw new IOException("ContentCollision");
        Directory.CreateDirectory(SafePath.Join(Root, "instances"));
        var destination = CompactProfilePath(profile);
        EnsureIdentity(stagedDirectory, profile.Id);
        Directory.Move(stagedDirectory, destination);
        return destination;
    }

    private string? FindProfileFolder(Guid id)
    {
        var instances = SafePath.Join(Root, "instances");
        if (!Directory.Exists(instances)) return null;
        var matches = Directory.EnumerateFileSystemEntries(instances).Where(path =>
            IsLegacyProfileRelative(id, "instances/" + Path.GetFileName(path)) ||
            Directory.Exists(path) && HasIdentity(path, id)).Take(2).ToArray();
        if (matches.Length > 1)
            throw new IOException("More than one folder matches this profile identifier.");
        var selected = matches.SingleOrDefault();
        if (selected is null) return null;
        SafePath.RejectLinks(selected);
        if (!Directory.Exists(selected)) throw new IOException("The profile folder path is occupied by a file.");
        return selected;
    }
    public void Initialize()
    {
        foreach (var dir in new[] { Root, Cache, Logs, Secrets, Backups, Path.Combine(Root, "instances") })
        {
            SafePath.RejectLinks(dir);
            Directory.CreateDirectory(dir);
        }
        MarkDataRoot();
    }
    private void MarkDataRoot()
    {
        var marker = SafePath.Join(Root, Security.UserDataCleaner.MarkerName);
        if (!File.Exists(marker)) File.WriteAllText(marker, Security.UserDataCleaner.MarkerText);
    }
    public void CreateProfile(GameProfile profile) => EnsureProfileDirectory(profile);

    internal string? EnsureProfileDirectory(GameProfile profile)
    {
        ProfileRules.Validate(profile);
        var existing = FindProfileFolder(profile.Id);
        if (existing is not null)
        {
            CreateGameFolders(existing);
            return null;
        }
        var stage = SafePath.Join(Root, ".pteno-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            CreateGameFolders(stage);
            return PlaceProfileDirectory(profile, stage);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                SafePath.RejectLinks(stage);
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    internal void RemoveEmptyCreatedProfile(Guid id, string directory)
    {
        if (FindProfileFolder(id) != directory || !HasIdentity(directory, id)) throw new IOException("ContentChanged");
        var game = SafePath.Join(directory, "game");
        var folders = GameFolders.Select(name => SafePath.Join(game, name)).ToArray();
        if (Directory.EnumerateFileSystemEntries(directory).Count() != 2 ||
            Directory.EnumerateFileSystemEntries(game).Count() != folders.Length ||
            folders.Any(folder => !Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()))
            throw new IOException("ContentChanged");
        foreach (var folder in folders) Directory.Delete(folder);
        Directory.Delete(game);
        File.Delete(SafePath.Join(directory, IdentityFile(id)));
        Directory.Delete(directory);
    }

    private static readonly string[] GameFolders = ["mods", "saves", "resourcepacks", "shaderpacks", "config"];
    private static void CreateGameFolders(string profile)
    {
        foreach (var sub in GameFolders)
            Directory.CreateDirectory(SafePath.Join(profile, "game/" + sub));
    }
    public static string InstalledRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PTenoLauncher");
    public static string PortableRoot => Path.Combine(AppContext.BaseDirectory, "portable-data");
    public static LauncherPaths Default(string[] args) => Resolve(args, AppContext.BaseDirectory, InstalledRoot);

    internal static LauncherPaths Resolve(string[] args, string applicationDirectory, string installedRoot)
    {
        var index = Array.IndexOf(args, "--data-root");
        if (index >= 0)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal) || Array.LastIndexOf(args, "--data-root") != index)
                throw new ArgumentException("Invalid --data-root.");
            return new(args[index + 1]);
        }
        return new(File.Exists(Path.Combine(applicationDirectory, "portable.flag"))
            ? Path.Combine(applicationDirectory, "portable-data") : installedRoot);
    }
}
