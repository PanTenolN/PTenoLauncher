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
    public string Profile(Guid id) => SafePath.Join(Root, $"instances/{id:N}");
    public string Game(Guid id) => SafePath.Join(Root, $"instances/{id:N}/game");
    public string Receipt(Guid id) => SafePath.Join(Root, $"instances/{id:N}/installation.json");
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
        foreach (var sub in new[] { "mods", "saves", "resourcepacks", "shaderpacks", "config" })
            Directory.CreateDirectory(SafePath.Join(Root, $"instances/{profile.Id:N}/game/{sub}"));
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