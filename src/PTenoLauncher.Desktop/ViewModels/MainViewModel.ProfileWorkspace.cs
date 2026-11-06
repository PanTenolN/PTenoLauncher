using System.Collections.ObjectModel;
using System.Globalization;
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
    public bool CanUseOnlineCatalog => IsIdle && !Offline && CanManageMods;
    public bool WorkspaceConsole => IsProfileWorkspace && WorkspaceSection == ProfileSection.Console;
    public bool WorkspaceFolders => IsProfileWorkspace && WorkspaceSection == ProfileSection.Folders;
    public bool CanLeaveWorkspace => IsIdle || IsGameLaunchActive;
    public ObservableCollection<string> WorkspaceInstalledMods { get; } = [];
    public ObservableCollection<WorkspaceModRow> WorkspaceModRows { get; } = [];
    private WorkspaceModRow[] displayedModRows = [];
    private string workspaceModQuery = "";
    public string WorkspaceModQuery { get => workspaceModQuery; set { if (Set(ref workspaceModQuery, value)) FilterWorkspaceModRows(); } }
    private void FilterWorkspaceModRows()
    {
        var rows = displayedModRows.Where(row => row.Name.Contains(WorkspaceModQuery ?? "", StringComparison.OrdinalIgnoreCase) ||
            row.File.Contains(WorkspaceModQuery ?? "", StringComparison.OrdinalIgnoreCase));
        WorkspaceModRows.Clear();
        var alphabetical = StringComparer.Create(CultureInfo.GetCultureInfo(TextCatalog.Language), ignoreCase: true);
        foreach (var row in rows.OrderBy(r => r.Name, alphabetical)
            .ThenBy(r => r.File, StringComparer.OrdinalIgnoreCase))
            WorkspaceModRows.Add(row);
    }

    private async Task<WorkspaceSnapshot> ReadWorkspaceModsAsync(Guid id, ModLoader loader)
    {
        var folder = SafePath.Join(services.Paths.Game(id), "mods");
        var unknownShader = TextCatalog.Get("ContentShaderComponentUnknown");
        if (!Directory.Exists(folder)) return new([], [], unknownShader);
        var entries = Directory.EnumerateFiles(folder)
            .Where(file => (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) &&
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
            .Order(StringComparer.OrdinalIgnoreCase).Take(1000).Select(file => Path.GetFileName(file)!).ToArray();
        IReadOnlyDictionary<string, InstalledModFile> inventory;
        try
        {
            var saved = await new ModInventoryStore(services.Paths).ReadAsync(id);
            inventory = (saved?.Files ?? []).ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            services.Log.Error("workspace.mod.inventory", error);
            inventory = new Dictionary<string, InstalledModFile>();
        }
        var rows = new List<WorkspaceModRow>(entries.Length);
        string? shader = null;
        var inspected = 0;
        foreach (var file in entries)
        {
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var row = new WorkspaceModRow(file, file, "-", "-", disabled ? "✗" : "✓");
            if (!disabled && inspected++ < 200)
            {
                try
                {
                    var mod = await services.Mods.InspectAsync(SafePath.Join(folder, file));
                    var origin = inventory.TryGetValue("mods/" + file, out var tracked) && tracked.Sha256.Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase)
                        ? tracked.Origin : null;
                    var source = origin switch { "modrinth" => "Modrinth", "local-modrinth" => TextCatalog.Get("Message048"),
                        "local" => TextCatalog.Get("Message047"), _ => "-" };
                    row = row with { Name = mod.Name ?? file, Version = mod.Version ?? "-", Source = source };
                    if (shader is null && loader != ModLoader.Vanilla && mod.Loader == loader && mod.Id is "iris" or "oculus" or "optifine")
                        shader = TextCatalog.Format("ContentShaderComponentFound", mod.Name ?? mod.Id, mod.Version ?? "");
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.Text.Json.JsonException)
                { services.Log.Error("workspace.mod.metadata", error); }
            }
            rows.Add(row);
        }
        return new(rows.ToArray(), [], shader ?? unknownShader);
    }
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
        _ = LoadWorkingSummaryAsync(profile.Id);
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
    }

    private void RefreshWorkspaceView()
    {
        foreach (var name in new[] { nameof(IsProfileWorkspace), nameof(WorkspaceSettings),
            nameof(WorkspaceOverview), nameof(WorkspaceVersions), nameof(WorkspaceMods), nameof(WorkspaceConsole), nameof(WorkspaceFolders), nameof(WorkspaceModTableVisible),
            nameof(WorkspaceGamePath), nameof(WorkspaceProfilePath), nameof(WorkspaceVersion), nameof(PageTitle),
            nameof(WorkspaceResourcePacks), nameof(WorkspaceShaders), nameof(WorkspaceWorlds), nameof(WorkspaceContentVisible),
            nameof(WorkspaceContentHint), nameof(WorkspaceCanImportPack) })
            Changed(name);
        PresentWorkspaceCache();
        Changed(nameof(CanMarkWorking));
        Changed(nameof(WorkingSummary));
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
