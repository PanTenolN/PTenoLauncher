namespace PTenoLauncher.Infrastructure.Storage;

/// <summary>Cross-process lease outside profile data; never blocks moving an inactive profile directory.</summary>
public sealed class ProfileUsage(LauncherPaths paths)
{
    public IDisposable Acquire(Guid id)
    {
        var file = SafePath.Join(paths.Root, $"locks/{id:N}.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Профіль зараз використовується. Спочатку завершіть гру або іншу операцію."); }
    }
    public void CheckWorldLocks(Guid id)
    {
        var active = AtomicFile.ReadJsonAsync<ActiveGameRecord>(SafePath.Join(paths.Profile(id), "active-game.json")).GetAwaiter().GetResult();
        if (active is not null && ActiveGameRecord.IsRunning(active)) throw new InvalidOperationException("Процес Minecraft ще працює.");
        var saves = SafePath.Join(paths.Game(id), "saves");
        if (!Directory.Exists(saves)) return;
        void Walk(string directory)
        {
            SafePath.RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                SafePath.RejectLinks(entry);
                if (Directory.Exists(entry)) Walk(entry);
                else if (Path.GetFileName(entry) == "session.lock")
                {
                    try { using var probe = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.None); }
                    catch (IOException) { throw new InvalidOperationException("Світ Minecraft відкрито іншим процесом."); }
                }
            }
        }
        Walk(saves);
    }
}