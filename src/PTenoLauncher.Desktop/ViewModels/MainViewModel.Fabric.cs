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
        SelectedFabricVersion = FabricVersions.FirstOrDefault(v => v.Version == receipt?.LoaderVersion) ?? FabricVersions.FirstOrDefault(v => v.Stable);
        FabricStatus = TextCatalog.Format("FabricCount", FabricVersions.Count) + (services.Fabric.UsedCachedMetadata ? TextCatalog.Get("Message032") : "");
        Status = TextCatalog.Get("Message033");
    });
}
