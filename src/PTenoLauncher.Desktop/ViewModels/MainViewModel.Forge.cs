using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<ForgeLoaderVersion> ForgeVersions { get; } = [];
    private ForgeLoaderVersion? selectedForgeVersion;
    private ForgePreparedComponents? forgePrepared;
    public ForgeLoaderVersion? SelectedForgeVersion
    {
        get => selectedForgeVersion;
        set { if (Set(ref selectedForgeVersion, value)) { forgePrepared = null; Changed(nameof(ForgeStatus)); Changed(nameof(CanInstallGame)); } }
    }
    public bool IsForgeProfile => SelectedProfile is { Loader: ModLoader.Forge or ModLoader.NeoForge };
    public bool CanInstallGame => IsIdle && (SelectedProfile is { Loader: ModLoader.Vanilla or ModLoader.Fabric or ModLoader.Quilt } || IsForgeProfile && (SelectedForgeVersion is not null || SelectedProfile?.PreferredLoaderVersion is not null));
    public string ForgeHint => TextCatalog.Get(SelectedProfile?.Loader == ModLoader.NeoForge ? "ForgeUnknownMinecraft" : "ForgeHint");
    public string ForgeStatus => forgePrepared is { } prepared
        ? TextCatalog.Format("ForgePrepared", prepared.Files.Count, prepared.Plan.ClientProcessors.Count, prepared.Plan.Libraries.Count(l => l.Generated), prepared.Plan.FmlCoordinate ?? "?")
        : ForgeVersions.Count > 0 ? TextCatalog.Format("ForgeCount", ForgeVersions.Count) : TextCatalog.Get("ForgeHint");
    private void ResetForgeSelection()
    { ForgeVersions.Clear(); SelectedForgeVersion = null; forgePrepared = null; Changed(nameof(ForgeStatus)); Changed(nameof(ForgeHint)); }
    public Task LoadForgeVersionsAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        if (profile.Loader is not (ModLoader.Forge or ModLoader.NeoForge)) throw new InvalidOperationException(TextCatalog.Get("ForgeSelect"));
        var versions = await services.ForgeVersions.GetVersionsAsync(profile.Loader, profile.MinecraftVersion, refresh: true, ct: ct);
        ForgePreparedComponents? previous = null;
        try { previous = await AtomicFile.ReadJsonAsync<ForgePreparedComponents>(SafePath.Join(services.Paths.Profile(profile.Id), "loader-components.json"), ct); }
        catch (System.Text.Json.JsonException e) { services.Log.Error("forge.preparation", e); }
        if (SelectedProfile?.Id != profile.Id) return;
        ResetForgeSelection(); foreach (var version in versions) ForgeVersions.Add(version);
        var requested = await PTenoLauncher.Infrastructure.Modpacks.MrpackRequirements.RequestedAsync(services.Paths, profile, ct: ct);
        SelectedForgeVersion = requested is not null ? ForgeVersions.FirstOrDefault(v => v.Version == requested) : ForgeVersions.FirstOrDefault(v => v.Version == previous?.Plan?.LoaderVersion && v.Loader == previous?.Plan?.Loader) ?? ForgeVersions.FirstOrDefault();
        Changed(nameof(ForgeStatus)); Status = TextCatalog.Format("ForgeCount", ForgeVersions.Count);
        if (requested is not null && SelectedForgeVersion is null) Status = TextCatalog.Get("PackTarget") + " " + requested;
    });
    public Task PrepareForgeComponentsAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        if (!IsForgeProfile || !UsesSavedLoaderVersion && SelectedForgeVersion?.Loader != profile.Loader)
            throw new InvalidOperationException(TextCatalog.Get("ForgeSelect"));
        var version = profile.PreferredLoaderVersion ?? SelectedForgeVersion?.Version ?? throw new InvalidOperationException(TextCatalog.Get("ForgeSelect"));
        _ = await PTenoLauncher.Infrastructure.Modpacks.MrpackRequirements.RequestedAsync(services.Paths, profile, version, ct);
        var prepared = await services.ForgeComponents.PrepareAsync(profile, version, ct: ct);
        if (SelectedProfile?.Id != profile.Id) return;
        forgePrepared = prepared; Changed(nameof(ForgeStatus)); Status = ForgeStatus;
    });
}
