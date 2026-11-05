using PTenoLauncher.Desktop.Localization;
using System.Collections.ObjectModel;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public ObservableCollection<FabricLoaderVersion> FabricVersions { get; } = [];
    private FabricLoaderVersion? selectedFabricVersion;
    public FabricLoaderVersion? SelectedFabricVersion { get => selectedFabricVersion; set => Set(ref selectedFabricVersion, value); }
    private static string DefaultFabricStatus => TextCatalog.Get("Message030");
    private string fabricStatus = DefaultFabricStatus;
    public string FabricStatus { get => TextCatalog.Relocalize(fabricStatus); set => Set(ref fabricStatus, value); }
    public bool IsFabricProfile => SelectedProfile is { Loader: ModLoader.Fabric };
    private void ResetFabricSelection() { FabricVersions.Clear(); SelectedFabricVersion = null; FabricStatus = DefaultFabricStatus; }
    public Task RememberSelectedFabricVersionAsync(FabricLoaderVersion version) => RunAsync(async ct =>
    {
        if (SelectedProfile is { Loader: ModLoader.Fabric } profile &&
            FabricVersions.Contains(version) && profile.PreferredLoaderVersion is null)
            await SaveFabricChoiceAsync(profile, version.Version, ct);
    });

    private async Task SaveFabricChoiceAsync(GameProfile profile, string version, CancellationToken ct)
    {
        if (await PTenoLauncher.Infrastructure.Modpacks.MrpackRequirements.ReadAsync(services.Paths, profile, ct) is not null) return;
        await services.Store.SaveProfileAsync(profile with { PreferredLoaderVersion = version }, ct);
        await ReloadProfilesAsync(ct);
    }
    public Task LoadFabricVersionsAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        if (profile.Loader != ModLoader.Fabric) throw new InvalidOperationException(TextCatalog.Get("Message031"));
        var versions = await services.Fabric.GetVersionsAsync(profile.MinecraftVersion, ct);
        InstallationReceipt? receipt = null;
        try { receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(services.Paths.Receipt(profile.Id), ct); }
        catch (System.Text.Json.JsonException ex) { services.Log.Error("fabric.receipt", ex); }
        if (SelectedProfile?.Id != profile.Id) return;
        FabricVersions.Clear(); foreach (var version in versions.OrderBy(v => v.Stable ? 0 : 1)) FabricVersions.Add(version);
        var requested = await PTenoLauncher.Infrastructure.Modpacks.MrpackRequirements.RequestedAsync(services.Paths, profile, ct: ct);
        SelectedFabricVersion = requested is not null ? FabricVersions.FirstOrDefault(v => v.Version == requested) : FabricVersions.FirstOrDefault(v => v.Version == receipt?.LoaderVersion) ?? FabricVersions.FirstOrDefault(v => v.Stable);
        FabricStatus = TextCatalog.Format("FabricCount", FabricVersions.Count) + (services.Fabric.UsedCachedMetadata ? TextCatalog.Get("Message032") : "");
        Status = TextCatalog.Get("Message033");
        if (requested is not null && SelectedFabricVersion is null) Status = TextCatalog.Get("PackTarget") + " " + requested;
    });
}
