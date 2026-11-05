using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Desktop.ViewModels;

public enum ProfileSection { Overview, Versions, Settings, Mods, Console, Folders, ResourcePacks, Shaders, Worlds }
public sealed record WorkspaceModRow(string File, string Name, string Version, string Source, string State);

public sealed partial class MainViewModel
{
    public Guid? WorkspaceProfileId { get; private set; }
    private LauncherPage workspaceReturnPage;
    private Guid? workspaceReturnProfile;
    public bool IsProfileWorkspace => Page == LauncherPage.ProfileWorkspace;
    public bool HasModsWorkspace => IsMods || IsProfileWorkspace;
    private ProfileSection workspaceSection;
    public ProfileSection WorkspaceSection
    {
        get => workspaceSection;
        private set { if (Set(ref workspaceSection, value)) RefreshWorkspaceView(); }
    }
    public bool WorkspaceSettings => IsProfileWorkspace && WorkspaceSection == ProfileSection.Settings;
    public bool WorkspaceOverview => IsProfileWorkspace && WorkspaceSection == ProfileSection.Overview;
    public bool WorkspaceVersions => IsProfileWorkspace && WorkspaceSection == ProfileSection.Versions;
    public bool WorkspaceMods => IsProfileWorkspace && WorkspaceSection == ProfileSection.Mods;
    public bool WorkspaceModTableVisible => WorkspaceMods && CanManageMods;
    public bool IsGlobalMods => Page == LauncherPage.Mods && CanManageMods;
    public bool CanUseOnlineCatalog => IsIdle && !Offline && CanManageMods;
    public bool WorkspaceConsole => IsProfileWorkspace && WorkspaceSection == ProfileSection.Console;
    public bool WorkspaceFolders => IsProfileWorkspace && WorkspaceSection == ProfileSection.Folders;
    public bool CanLeaveWorkspace => IsIdle || IsGameLaunchActive;
    public ObservableCollection<string> WorkspaceInstalledMods { get; } = [];
    public ObservableCollection<WorkspaceModRow> WorkspaceModRows { get; } = [];
    private WorkspaceModRow[] modMetadata = [];
    private Guid? modMetadataProfile;
    private string workspaceModQuery = "";
    private bool modDescending;
    public string WorkspaceModQuery { get => workspaceModQuery; set { if (Set(ref workspaceModQuery, value)) FilterWorkspaceModRows(); } }
    public void SortWorkspaceMods() { modDescending = !modDescending; FilterWorkspaceModRows(); }
    private void FilterWorkspaceModRows()
    {
        var metadata = modMetadataProfile == WorkspaceProfileId ? modMetadata.ToDictionary(m => m.File, StringComparer.OrdinalIgnoreCase) : [];
        var rows = WorkspaceInstalledMods.Select(file => metadata.GetValueOrDefault(file) ??
            new(file, file, "—", "—", file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? "○" : "✓"))
            .Where(row => row.Name.Contains(WorkspaceModQuery ?? "", StringComparison.OrdinalIgnoreCase) ||
                row.File.Contains(WorkspaceModQuery ?? "", StringComparison.OrdinalIgnoreCase));
        WorkspaceModRows.Clear();
        foreach (var row in modDescending ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase) :
            rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) WorkspaceModRows.Add(row);
    }
    public Task InspectWorkspaceModsAsync() => RunAsync(async ct =>
    {
        var id = WorkspaceProfileId;
        if (id is null || !WorkspaceModTableVisible) return;
        RefreshWorkspaceMods();
        var entries = new List<WorkspaceModRow>();
        IReadOnlyDictionary<string, InstalledModFile> inventory;
        try
        {
            var saved = await new ModInventoryStore(services.Paths).ReadAsync(id.Value, ct);
            inventory = (saved?.Files ?? []).ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            services.Log.Error("workspace.mod.inventory", error);
            inventory = new Dictionary<string, InstalledModFile>();
        }
        foreach (var file in WorkspaceInstalledMods.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).Take(200).ToArray())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var mod = await services.Mods.InspectAsync(SafePath.Join(services.Paths.Game(id.Value), "mods/" + file), ct);
                var origin = inventory.TryGetValue("mods/" + file, out var tracked) && tracked.Sha256.Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? tracked.Origin : null;
                var source = origin switch { "modrinth" => "Modrinth", "local-modrinth" => TextCatalog.Get("Message048"),
                    "local" => TextCatalog.Get("Message047"), _ => "—" };
                entries.Add(new(file, mod.Name ?? file, mod.Version ?? "—", source, "✓"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            { services.Log.Error("workspace.mod.metadata", ex); }
        }
        if (WorkspaceProfileId != id) return;
        modMetadataProfile = id; modMetadata = entries.ToArray();
        FilterWorkspaceModRows();
    });
    public string WorkspaceGamePath => WorkspaceProfileId is { } id ? services.Paths.Game(id) : "";
    public string WorkspaceProfilePath => WorkspaceProfileId is { } id ? services.Paths.Profile(id) : "";
    private string? workspaceResolvedLoader;
    public string WorkspaceVersion => WorkspaceProfileId is not null && SelectedProfile is { } profile &&
        profile.PreferredLoaderVersion is null && workspaceResolvedLoader is { } exact
        ? (profile with { PreferredLoaderVersion = exact }).Summary : ActiveVersion;

    public void OpenProfileWorkspace(Guid profileId) => OpenProfileWorkspace(profileId, SelectedProfile?.Id);

    internal void OpenProfileWorkspace(Guid profileId, Guid? returnProfile)
    {
        if (!CanLeaveWorkspace) return;
        var profile = allProfiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;
        if (!IsProfileWorkspace)
        {
            workspaceReturnPage = Page;
            workspaceReturnProfile = returnProfile;
        }
        WorkspaceProfileId = null;
        SelectedProfile = profile;
        if (SelectedProfile?.Id != profileId) return;
        WorkspaceProfileId = profileId;
        workspaceResolvedLoader = null;
        WorkspaceSection = ProfileSection.Overview;
        Page = LauncherPage.ProfileWorkspace;
        RefreshWorkspaceView();
        _ = LoadWorkspaceVersionAsync(profile);
    }

    public void BackFromProfileWorkspace()
    {
        if (!IsProfileWorkspace || !CanLeaveWorkspace) return;
        var previous = allProfiles.FirstOrDefault(p => p.Id == workspaceReturnProfile);
        Page = workspaceReturnPage;
        if (previous is not null) SelectedProfile = previous;
    }

    private async Task LoadWorkspaceVersionAsync(GameProfile profile)
    {
        if (profile.Loader == ModLoader.Vanilla || profile.PreferredLoaderVersion is not null) return;
        try
        {
            var requested = await PTenoLauncher.Infrastructure.Modpacks.MrpackRequirements.RequestedAsync(services.Paths, profile);
            if (requested is null)
            {
                var installed = await AtomicFile.ReadJsonAsync<InstallationReceipt>(services.Paths.Receipt(profile.Id));
                if (installed?.Loader == profile.Loader && installed.Version == profile.MinecraftVersion)
                    requested = installed.LoaderVersion;
            }
            if (WorkspaceProfileId == profile.Id && SelectedProfile == profile)
            {
                workspaceResolvedLoader = requested;
                Changed(nameof(WorkspaceVersion));
            }
        }
        catch (Exception ex) { services.Log.Error("workspace.loader", ex); }
    }

    public void ChooseWorkspaceSection(ProfileSection section)
    {
        if (WorkspaceProfileId is null) return;
        WorkspaceSection = section;
        if (section == ProfileSection.Mods) RefreshWorkspaceMods();
    }

    private void RefreshWorkspaceView()
    {
        foreach (var name in new[] { nameof(IsProfileWorkspace), nameof(HasModsWorkspace), nameof(WorkspaceSettings),
            nameof(WorkspaceOverview), nameof(WorkspaceVersions), nameof(WorkspaceMods), nameof(WorkspaceConsole), nameof(WorkspaceFolders), nameof(IsMods), nameof(IsGlobalMods), nameof(WorkspaceModTableVisible),
            nameof(WorkspaceGamePath), nameof(WorkspaceProfilePath), nameof(WorkspaceVersion), nameof(PageTitle),
            nameof(WorkspaceResourcePacks), nameof(WorkspaceShaders), nameof(WorkspaceWorlds), nameof(WorkspaceContentVisible),
            nameof(WorkspaceContentHint), nameof(WorkspaceCanImportPack) })
            Changed(name);
    }

    private void RefreshWorkspaceMods()
    {
        if (!WorkspaceMods || WorkspaceProfileId is not { } id) return;
        WorkspaceInstalledMods.Clear();
        try
        {
            var mods = SafePath.Join(services.Paths.Game(id), "mods");
            if (!Directory.Exists(mods)) { FilterWorkspaceModRows(); return; }
            foreach (var file in Directory.EnumerateFiles(mods).Where(file =>
                (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) &&
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0).Order(StringComparer.OrdinalIgnoreCase).Take(1000))
                WorkspaceInstalledMods.Add(Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = ex.Message; }
        FilterWorkspaceModRows();
    }

    internal string WorkspaceFolder(string kind)
    {
        var id = WorkspaceProfileId ?? throw new InvalidOperationException(TextCatalog.Get("Message022"));
        var folder = kind switch
        {
            "game" => services.Paths.Game(id),
            "mods" => SafePath.Join(services.Paths.Game(id), "mods"),
            "logs" => SafePath.Join(services.Paths.Profile(id), "launcher-logs"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        SafePath.RejectLinks(folder);
        return folder;
    }

    public void OpenWorkspaceFolder(string kind)
    {
        try
        {
            var folder = WorkspaceFolder(kind);
            if (!Directory.Exists(folder))
            {
                Status = TextCatalog.Get(kind == "logs" ? "WorkspaceNoLogs" : "WorkspaceNoFolder");
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { Status = ex.Message; }
    }
}
