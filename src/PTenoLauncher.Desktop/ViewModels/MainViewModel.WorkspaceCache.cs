using Avalonia.Threading;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly record struct WorkspaceKey(Guid Profile, ProfileSection Section);
    private sealed record WorkspaceSnapshot(WorkspaceModRow[] Mods, ProfileContentEntry[] Content, string? Shader = null);
    private sealed class WorkspaceCacheEntry
    {
        public WorkspaceSnapshot? Snapshot;
        public long Revision;
        public long LoadedRevision = -1;
        public Task? Pending;
        public string? Select;
        public ModLoader? Loader;
    }

    private static readonly ProfileSection[] FileSections =
        [ProfileSection.Mods, ProfileSection.ResourcePacks, ProfileSection.Shaders, ProfileSection.Worlds];
    private readonly Dictionary<WorkspaceKey, WorkspaceCacheEntry> workspaceCache = [];
    private readonly Dictionary<Guid, FileSystemWatcher> workspaceWatchers = [];
    private readonly HashSet<WorkspaceKey> pendingWorkspaceChanges = [];
    private bool workspaceWatching, workspaceChangePosted;
    private WorkspaceKey? displayedWorkspace;

    private WorkspaceKey? VisibleWorkspaceKey => IsProfileWorkspace && WorkspaceProfileId is { } profile &&
        FileSections.Contains(WorkspaceSection) ? new(profile, WorkspaceSection) : null;

    private static ProfileContentKind ContentKind(ProfileSection section) => section switch
    {
        ProfileSection.ResourcePacks => ProfileContentKind.ResourcePacks,
        ProfileSection.Shaders => ProfileContentKind.Shaders,
        ProfileSection.Worlds => ProfileContentKind.Worlds,
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    private WorkspaceCacheEntry CacheFor(WorkspaceKey key)
    {
        if (workspaceCache.TryGetValue(key, out var entry)) return entry;
        entry = new();
        workspaceCache.Add(key, entry);
        return entry;
    }

    private void PresentWorkspaceCache()
    {
        var key = VisibleWorkspaceKey;
        if (displayedWorkspace != key)
        {
            displayedWorkspace = key;
            if (key is { } visible) PublishWorkspaceSnapshot(visible, CacheFor(visible).Snapshot, preserveSelection: false);
        }
        if (key is { } current)
        {
            WatchWorkspace(current.Profile);
            _ = EnsureWorkspaceSnapshotAsync(current);
        }
    }

    internal Task WaitForWorkspaceSnapshotAsync() => VisibleWorkspaceKey is { } key
        ? EnsureWorkspaceSnapshotAsync(key) : Task.CompletedTask;

    internal Task RefreshVisibleWorkspaceAsync() => VisibleWorkspaceKey is { } key
        ? key.Section is ProfileSection.Mods or ProfileSection.Shaders ? InvalidateWorkspaceModsAsync(key.Profile) : InvalidateWorkspaceAsync(key)
        : Task.CompletedTask;

    private Task EnsureWorkspaceSnapshotAsync(WorkspaceKey key)
    {
        var entry = CacheFor(key);
        var loader = (SelectedProfile?.Id == key.Profile ? SelectedProfile : allProfiles.FirstOrDefault(profile => profile.Id == key.Profile))?.Loader ?? ModLoader.Vanilla;
        if (entry.Loader != loader)
        {
            entry.Loader = loader;
            entry.Revision++;
        }
        if (entry.Pending is { } pending) return pending;
        if (entry.LoadedRevision == entry.Revision) return Task.CompletedTask;
        return entry.Pending = ScanWorkspaceAsync(key, entry);
    }

    private async Task ScanWorkspaceAsync(WorkspaceKey key, WorkspaceCacheEntry entry)
    {
        await Task.Yield();
        try
        {
            while (entry.LoadedRevision != entry.Revision)
            {
                var revision = entry.Revision;
                var loader = entry.Loader ?? ModLoader.Vanilla;
                WorkspaceSnapshot snapshot;
                if (key.Section == ProfileSection.Mods)
                    snapshot = await Task.Run(() => ReadWorkspaceModsAsync(key.Profile, loader));
                else
                {
                    var kind = ContentKind(key.Section);
                    var rows = await Task.Run(() => services.Content.ListAsync(key.Profile, kind));
                    string? shader = null;
                    if (kind == ProfileContentKind.Shaders)
                    {
                        var mods = new WorkspaceKey(key.Profile, ProfileSection.Mods);
                        if (loader != ModLoader.Vanilla) await EnsureWorkspaceSnapshotAsync(mods);
                        shader = CacheFor(mods).Snapshot?.Shader ?? TextCatalog.Get("ContentShaderComponentUnknown");
                    }
                    snapshot = new([], rows.ToArray(), shader);
                }
                if (!workspaceCache.TryGetValue(key, out var current) || current != entry) return;
                if (revision != entry.Revision) continue;
                entry.Snapshot = snapshot;
                entry.LoadedRevision = revision;
                if (VisibleWorkspaceKey == key)
                {
                    PublishWorkspaceSnapshot(key, snapshot, preserveSelection: true, entry.Select);
                    entry.Select = null;
                }
            }
        }
        catch (Exception error)
        {
            services.Log.Error("workspace.snapshot", error);
            if (VisibleWorkspaceKey == key) Status = error.Message;
        }
        finally { entry.Pending = null; }
    }

    private void PublishWorkspaceSnapshot(WorkspaceKey key, WorkspaceSnapshot? snapshot, bool preserveSelection, string? select = null)
    {
        if (key.Section == ProfileSection.Mods)
        {
            displayedModRows = snapshot?.Mods ?? [];
            WorkspaceInstalledMods.Clear();
            foreach (var row in displayedModRows) WorkspaceInstalledMods.Add(row.File);
            FilterWorkspaceModRows();
            Changed(nameof(ActiveMods));
        }
        else
        {
            var selected = select ?? (preserveSelection ? WorkspaceContentSelection?.FileName : null);
            WorkspaceContentRows.Clear();
            foreach (var row in snapshot?.Content ?? []) WorkspaceContentRows.Add(row);
            WorkspaceContentSelection = WorkspaceContentRows.FirstOrDefault(row => row.FileName == selected);
            WorkspaceShaderSupport = snapshot?.Shader ?? "";
        }
    }

    private Task InvalidateWorkspaceAsync(WorkspaceKey key, string? select = null)
    {
        lock (pendingWorkspaceChanges) pendingWorkspaceChanges.Remove(key);
        if (workspaceCache.TryGetValue(key, out var entry))
        {
            entry.Revision++;
            if (select is not null) entry.Select = select;
        }
        return VisibleWorkspaceKey == key ? EnsureWorkspaceSnapshotAsync(key) : Task.CompletedTask;
    }

    internal Task InvalidateWorkspaceModsAsync(Guid profile)
    {
        var mods = InvalidateWorkspaceAsync(new(profile, ProfileSection.Mods));
        var shaders = InvalidateWorkspaceAsync(new(profile, ProfileSection.Shaders));
        return Task.WhenAll(mods, shaders);
    }

    internal void EnableWorkspaceWatching()
    {
        workspaceWatching = true;
        if (VisibleWorkspaceKey is { } key) WatchWorkspace(key.Profile);
    }

    internal void DisableWorkspaceWatching()
    {
        workspaceWatching = false;
        foreach (var watcher in workspaceWatchers.Values) watcher.Dispose();
        workspaceWatchers.Clear();
        workspaceCache.Clear();
        displayedWorkspace = null;
        lock (pendingWorkspaceChanges) pendingWorkspaceChanges.Clear();
    }

    internal void WorkspaceFolderMoved(Guid profile)
    {
        if (workspaceWatchers.Remove(profile, out var watcher)) watcher.Dispose();
        WatchWorkspace(profile);
    }

    private void WatchWorkspace(Guid profile)
    {
        if (!workspaceWatching || workspaceWatchers.ContainsKey(profile)) return;
        try
        {
            var root = services.Paths.Game(profile);
            if (!Directory.Exists(root)) return;
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += (_, args) => RecordWorkspaceChange(profile, args.Name, false);
            watcher.Deleted += (_, args) => RecordWorkspaceChange(profile, args.Name, false);
            watcher.Changed += (_, args) => RecordWorkspaceChange(profile, args.Name, true);
            watcher.Renamed += (_, args) =>
            {
                RecordWorkspaceChange(profile, args.OldName, false);
                RecordWorkspaceChange(profile, args.Name, false);
            };
            watcher.Error += (_, _) =>
            {
                foreach (var section in FileSections) QueueWorkspaceChange(new(profile, section));
            };
            try { watcher.EnableRaisingEvents = true; }
            catch { watcher.Dispose(); throw; }
            workspaceWatchers.Add(profile, watcher);
            if (workspaceWatchers.Count > 8)
            {
                var oldest = workspaceWatchers.Keys.First(id => id != profile);
                workspaceWatchers[oldest].Dispose();
                workspaceWatchers.Remove(oldest);
                foreach (var section in FileSections) workspaceCache.Remove(new(oldest, section));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { services.Log.Error("workspace.watch", error); }
    }

    private void RecordWorkspaceChange(Guid profile, string? path, bool changed)
    {
        if (path is null) return;
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(static part => part.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase))) return;
        var section = parts[0].ToLowerInvariant() switch
        {
            "mods" when parts.Length == 1 || parts.Length == 2 &&
                (parts[1].EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || parts[1].EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) => ProfileSection.Mods,
            "resourcepacks" when parts.Length <= 2 || parts.Length == 3 &&
                parts[2].Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase) => ProfileSection.ResourcePacks,
            "shaderpacks" when parts.Length <= 2 => ProfileSection.Shaders,
            "saves" when !changed && (parts.Length <= 2 || parts.Length == 3 &&
                parts[2].Equals("level.dat", StringComparison.OrdinalIgnoreCase)) => ProfileSection.Worlds,
            _ => (ProfileSection?)null
        };
        if (section is not { } relevant) return;
        QueueWorkspaceChange(new(profile, relevant));
        if (relevant == ProfileSection.Mods) QueueWorkspaceChange(new(profile, ProfileSection.Shaders));
    }

    private void QueueWorkspaceChange(WorkspaceKey key)
    {
        lock (pendingWorkspaceChanges)
        {
            pendingWorkspaceChanges.Add(key);
            if (workspaceChangePosted) return;
            workspaceChangePosted = true;
        }
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(180);
            WorkspaceKey[] changes;
            lock (pendingWorkspaceChanges)
            {
                changes = pendingWorkspaceChanges.ToArray();
                pendingWorkspaceChanges.Clear();
                workspaceChangePosted = false;
            }
            if (!workspaceWatching) return;
            foreach (var change in changes.OrderBy(key => key.Section))
                if (workspaceWatchers.ContainsKey(change.Profile)) _ = InvalidateWorkspaceAsync(change);
        }, DispatcherPriority.Background);
    }
}
