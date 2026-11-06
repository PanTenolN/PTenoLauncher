using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.ViewModels;

internal sealed record ContentCartPick(CatalogSearchHit Project, CatalogModVersion Version, bool Installed = false, string Result = "")
{
    public string Title => Project.Title;
    public string VersionLabel => Version.Number + " · " + Version.Id;
    public string FileName => Version.File.Name;
}

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

    private Task ReloadWorkspaceContentAsync(Guid id, ProfileContentKind kind, string? select = null)
    {
        var section = kind switch
        {
            ProfileContentKind.ResourcePacks => ProfileSection.ResourcePacks,
            ProfileContentKind.Shaders => ProfileSection.Shaders,
            ProfileContentKind.Worlds => ProfileSection.Worlds,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return InvalidateWorkspaceAsync(new(id, section), select);
    }
    internal Task ImportWorkspacePackAsync(Guid id, ProfileContentKind kind, string file) => RunAsync(async ct =>
    {
        if (WorkspaceProfileId != id || WorkspaceContentKind != kind || kind == ProfileContentKind.Worlds || IsGameLaunchActive) return;
        ProfileContentEntry imported;
        try { imported = await services.Content.ImportAsync(id, kind, file, ct); }
        catch (Exception error) when (error is IOException or InvalidDataException)
        { throw new InvalidOperationException(TextCatalog.Get(error.Message.StartsWith("Content", StringComparison.Ordinal)
            ? error.Message : "ContentInvalidZip"), error); }
        await ReloadWorkspaceContentAsync(id, kind, imported.FileName);
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
        if (remove) await ReloadWorkspaceContentAsync(plan.Profile, plan.Kind);
        Status = TextCatalog.Format(remove ? "ContentRemoved" : "ContentCopied", plan.Target);
    });

    internal Task InstallWorkspaceContentAsync(GameProfile profile, ProfileContentKind kind, IReadOnlyList<ContentCartPick> picks,
        Action<ContentCartPick, ProfileContentEntry?, string> report) =>
        RunAsync(async ct =>
        {
            if (WorkspaceProfileId != profile.Id || SelectedProfile != profile || WorkspaceContentKind != kind ||
                IsGameLaunchActive || kind == ProfileContentKind.Worlds || picks.Count is < 1 or > 64 ||
                picks.Any(pick => pick.Installed || pick.Version.ProjectId != pick.Project.Id ||
                    !pick.Version.GameVersions.Contains(profile.MinecraftVersion, StringComparer.Ordinal)))
                throw new InvalidOperationException(TextCatalog.Get("ContentChanged"));
            var installed = 0;
            string? last = null;
            try
            {
                foreach (var pick in picks)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var result = await TrackDownloadsAsync(pick.Title, profile.Name, async progress =>
                        {
                            var source = await services.Modrinth.DownloadContentAsync(pick.Version, progress, ct);
                            return await services.Content.ImportAsync(profile.Id, kind, source, ct, pick.FileName, pick.Version.File.Sha512, pick.Title);
                        }, ct);
                        installed++;
                        last = result.FileName;
                        report(pick, result, TextCatalog.Format("ContentAdded", result.FileName));
                    }
                    catch (Exception error) when (!ct.IsCancellationRequested && error is IOException or InvalidDataException or
                        InvalidOperationException or HttpRequestException or UnauthorizedAccessException or TaskCanceledException)
                    {
                        services.Log.Error("content.cart.install", error);
                        var reason = error switch
                        {
                            HttpRequestException { StatusCode: { } code } => TextCatalog.Format("ContentCartHttpFailed", (int)code),
                            HttpRequestException or TaskCanceledException => TextCatalog.Get("ContentCartNetworkFailed"),
                            _ when error.Message.StartsWith("Content", StringComparison.Ordinal) => TextCatalog.Get(error.Message),
                            InvalidDataException => TextCatalog.Get("ContentCatalogMismatch"),
                            _ => TextCatalog.Get("ContentCartFileFailed")
                        };
                        report(pick, null, reason);
                    }
                }
            }
            finally
            {
                if (installed != 0) await ReloadWorkspaceContentAsync(profile.Id, kind, last);
            }
            Status = TextCatalog.Format("ContentCartResult", installed, picks.Count - installed);
        });
}
