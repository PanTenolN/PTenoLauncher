using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public bool WorkspaceResourcePacks => IsProfileWorkspace && WorkspaceSection == ProfileSection.ResourcePacks;
    public bool WorkspaceShaders => IsProfileWorkspace && WorkspaceSection == ProfileSection.Shaders;
    public bool WorkspaceWorlds => IsProfileWorkspace && WorkspaceSection == ProfileSection.Worlds;
    public bool WorkspaceContentVisible => WorkspaceContentKind is not null;
    public bool WorkspaceCanImportPack => WorkspaceResourcePacks || WorkspaceShaders;
    public string WorkspaceShaderSupport { get => workspaceShaderSupport; private set => Set(ref workspaceShaderSupport, value); }
    private string workspaceShaderSupport = "";
    public string WorkspaceContentHint => TextCatalog.Get(WorkspaceResourcePacks ? "ContentResourceHint" :
        WorkspaceShaders ? "ContentShaderHint" : "ContentWorldHint");
    internal ProfileContentKind? WorkspaceContentKind => WorkspaceResourcePacks ? ProfileContentKind.ResourcePacks :
        WorkspaceShaders ? ProfileContentKind.Shaders : WorkspaceWorlds ? ProfileContentKind.Worlds : null;
    public ObservableCollection<ProfileContentEntry> WorkspaceContentRows { get; } = [];
    private ProfileContentEntry? workspaceContentSelection;
    public ProfileContentEntry? WorkspaceContentSelection
    {
        get => workspaceContentSelection;
        set { if (Set(ref workspaceContentSelection, value)) Changed(nameof(HasWorkspaceContentSelection)); }
    }
    public bool HasWorkspaceContentSelection => WorkspaceContentSelection is not null;

    public Task RefreshWorkspaceContentAsync() => RunAsync(async ct =>
    {
        if (WorkspaceProfileId is { } id && WorkspaceContentKind is { } kind)
            await LoadWorkspaceContentAsync(id, kind, ct);
    });

    private async Task LoadWorkspaceContentAsync(Guid id, ProfileContentKind kind, CancellationToken ct, string? select = null)
    {
        WorkspaceContentRows.Clear();
        WorkspaceContentSelection = null;
        var rows = await services.Content.ListAsync(id, kind, ct);
        if (WorkspaceProfileId != id || WorkspaceContentKind != kind) return;
        foreach (var item in rows) WorkspaceContentRows.Add(item);
        WorkspaceContentSelection = WorkspaceContentRows.FirstOrDefault(row => row.FileName == select);
        if (kind == ProfileContentKind.Shaders)
        {
            WorkspaceShaderSupport = TextCatalog.Get("ContentShaderComponentUnknown");
            if (SelectedProfile?.Loader == ModLoader.Vanilla) return;
            var mods = System.IO.Path.Combine(services.Paths.Game(id), "mods");
            if (!Directory.Exists(mods)) return;
            foreach (var jar in Directory.EnumerateFiles(mods, "*.jar").Take(200))
            {
                try
                {
                    var info = await services.Mods.InspectAsync(jar, ct);
                    if (info.Loader == SelectedProfile?.Loader && info.Id is "iris" or "oculus" or "optifine")
                    {
                        WorkspaceShaderSupport = TextCatalog.Format("ContentShaderComponentFound", info.Name ?? info.Id, info.Version ?? "");
                        break;
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { }
            }
        }
    }

    internal Task ImportWorkspacePackAsync(Guid id, ProfileContentKind kind, string file) => RunAsync(async ct =>
    {
        if (WorkspaceProfileId != id || WorkspaceContentKind != kind || kind == ProfileContentKind.Worlds || IsGameLaunchActive) return;
        ProfileContentEntry imported;
        try { imported = await services.Content.ImportAsync(id, kind, file, ct); }
        catch (Exception error) when (error is IOException or InvalidDataException)
        { throw new InvalidOperationException(TextCatalog.Get(error.Message.StartsWith("Content", StringComparison.Ordinal)
            ? error.Message : "ContentInvalidZip"), error); }
        await LoadWorkspaceContentAsync(id, kind, CancellationToken.None, imported.FileName);
        Status = TextCatalog.Format("ContentAdded", imported.Name);
    });

    internal Task ApplyWorkspaceContentAsync(ContentFileAction plan, bool remove, bool backup) => RunAsync(async ct =>
    {
        if (WorkspaceProfileId != plan.Profile || WorkspaceContentKind != plan.Kind || IsGameLaunchActive) return;
        try
        {
            if (remove) await services.Content.RemoveAsync(plan, backup, ct);
            else await services.Content.CopyAsync(plan, ct);
        }
        catch (IOException error) when (error.Message.StartsWith("Content", StringComparison.Ordinal))
        { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
        if (remove) await LoadWorkspaceContentAsync(plan.Profile, plan.Kind, CancellationToken.None);
        Status = TextCatalog.Format(remove ? "ContentRemoved" : "ContentCopied", plan.Target);
    });

    internal Task InstallWorkspaceContentAsync(GameProfile profile, ProfileContentKind kind, CatalogSearchHit hit, CatalogModVersion version) =>
        RunAsync(async ct =>
        {
            if (WorkspaceProfileId != profile.Id || SelectedProfile != profile || WorkspaceContentKind != kind ||
                IsGameLaunchActive || version.ProjectId != hit.Id || !version.GameVersions.Contains(profile.MinecraftVersion, StringComparer.Ordinal))
                throw new InvalidOperationException(TextCatalog.Get("ContentChanged"));
            ProfileContentEntry installed;
            try
            {
                installed = await TrackDownloadsAsync(hit.Title, profile.Name, async progress =>
                {
                    var source = await services.Modrinth.DownloadContentAsync(version, progress, ct);
                    return await services.Content.ImportAsync(profile.Id, kind, source, ct, version.File.Name, version.File.Sha512);
                }, ct);
            }
            catch (Exception error) when (error is IOException or InvalidDataException && error.Message.StartsWith("Content", StringComparison.Ordinal))
            { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
            await LoadWorkspaceContentAsync(profile.Id, kind, CancellationToken.None, installed.FileName);
            Status = TextCatalog.Format("ContentAdded", installed.Name);
        });
}
