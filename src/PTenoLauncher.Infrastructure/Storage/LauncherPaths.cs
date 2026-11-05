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
    internal static string NamedProfileRelative(GameProfile profile)
    {
        var slug = string.Concat(profile.Name.Take(48).Select(character => char.IsLetterOrDigit(character) ?
            char.ToLowerInvariant(character) : '-')).Trim('-');
        return $"instances/{(slug.Length == 0 ? "profile" : slug)}-{profile.Id:N}";
    }
    internal static bool IsProfileRelative(Guid id, string relative)
    {
        if (relative == LegacyProfileRelative(id)) return true;
        const string prefix = "instances/";
        var suffix = "-" + id.ToString("N");
        if (!relative.StartsWith(prefix, StringComparison.Ordinal) || !relative.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var slug = relative[prefix.Length..^suffix.Length];
        return slug.Length is > 0 and <= 48 && slug.All(c => char.IsLetterOrDigit(c) || c == '-');
    }
    internal string ProfileFolderForCreation(GameProfile profile) =>
        FindProfileFolder(profile.Id) ?? SafePath.Join(Root, NamedProfileRelative(profile));

    private string? FindProfileFolder(Guid id)
    {
        var instances = SafePath.Join(Root, "instances");
        if (!Directory.Exists(instances)) return null;
        var legacy = SafePath.Join(Root, LegacyProfileRelative(id));
        var named = Directory.EnumerateFileSystemEntries(instances, $"*-{id:N}", SearchOption.TopDirectoryOnly)
            .Where(path => IsProfileRelative(id, "instances/" + Path.GetFileName(path))).Take(2).ToArray();
        var legacyExists = Directory.Exists(legacy) || File.Exists(legacy);
        if (named.Length > 1 || named.Length == 1 && legacyExists)
            throw new IOException("More than one folder matches this profile identifier.");
        var selected = named.FirstOrDefault() ?? (legacyExists ? legacy : null);
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
    public void CreateProfile(GameProfile profile)
    {
        ProfileRules.Validate(profile);
        var game = SafePath.Join(ProfileFolderForCreation(profile), "game");
        foreach (var sub in new[] { "mods", "saves", "resourcepacks", "shaderpacks", "config" })
            Directory.CreateDirectory(SafePath.Join(game, sub));
    }
    public static LauncherPaths Default(string[] args)
    {
        var index = Array.IndexOf(args, "--data-root");
        if (index >= 0)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("--data-root потребує шляху.");
            return new(args[index + 1]);
        }
        return new(File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"))
            ? Path.Combine(AppContext.BaseDirectory, "portable-data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PTenoLauncher"));
    }
}
