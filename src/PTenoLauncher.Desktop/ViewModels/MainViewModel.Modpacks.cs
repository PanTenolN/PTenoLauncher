using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Modpacks;
namespace PTenoLauncher.Desktop.ViewModels;

public sealed class PackOptionalChoice(string path, Action invalidate) : ObservableObject
{
    public string Path { get; } = path;
    private bool selected;
    public bool Selected { get => selected; set { if (Set(ref selected, value)) invalidate(); } }
}
public sealed partial class MainViewModel
{
    private MrpackPreview? packPreview;
    private ZipPackPreview? zipPreview;
    private InstancePackPreview? instancePreview;
    private MinecraftLauncherPreview? minecraftPreview;
    private ModrinthAppPreview? modrinthAppPreview;
    private bool modrinthAppMode;
    private string modrinthInstanceId = "";
    private string? modrinthProfilesDirectory;
    public bool IsModrinthAppPack => modrinthAppMode;
    public ObservableCollection<ModrinthAppChoice> ModrinthAppChoices { get; } = [];
    public ModrinthAppChoice? SelectedModrinthAppProfile
    {
        get => ModrinthAppChoices.FirstOrDefault(c => c.Id == modrinthInstanceId);
        set
        {
            var id = value?.Id ?? "";
            if (id == modrinthInstanceId) return;
            modrinthInstanceId = id;
            packName = "";
            Changed();
            Changed(nameof(PackName));
            InvalidatePack();
        }
    }
    public string ModrinthAppContent => modrinthProfilesDirectory ?? "";
    private bool minecraftMode;
    private string minecraftProfileKey = "", minecraftContent = "";
    public bool IsMinecraftPack => minecraftMode;
    public ObservableCollection<MinecraftLauncherChoice> MinecraftProfileChoices { get; } = [];
    public MinecraftLauncherChoice? SelectedMinecraftImportProfile { get => MinecraftProfileChoices.FirstOrDefault(c => c.Key == minecraftProfileKey); set { if (value is not null && value.Key != minecraftProfileKey) { minecraftProfileKey=value.Key;packName="";minecraftContent=""; Changed();Changed(nameof(PackName));Changed(nameof(MinecraftImportContent));InvalidatePack(); } } }
    public string MinecraftImportContent => minecraftContent;
    public string MinecraftImportRequiredContent => minecraftPreview?.RequiredContentDirectory ?? "";
    private bool instanceMode;
    private bool curseForgeAppMode;
    public bool IsInstancePack => instanceMode;
    public bool IsCurseForgeAppPack => instanceMode && curseForgeAppMode;
    public string InstanceLayout => instancePreview is { } p ? TextCatalog.Format("InstanceLayoutLabel", p.Plan.ContentRoot, p.Plan.Files.Sum(f => f.Size) / 1048576.0) : "";
    private bool zipMode;
    private string zipMinecraft = "", zipLoaderVersion = "", zipRoot = "";
    private ModLoader zipLoader;
    public bool IsZipPack => zipMode;
    public string ZipMinecraft { get => zipMinecraft; set { if (Set(ref zipMinecraft, value)) InvalidatePack(); } }
    public string ZipLoaderVersion { get => zipLoaderVersion; set { if (Set(ref zipLoaderVersion, value)) InvalidatePack(); } }
    public ModLoader ZipLoader { get => zipLoader; set { if (Set(ref zipLoader, value)) InvalidatePack(); } }
    public ModLoader[] ZipLoaders { get; } = Enum.GetValues<ModLoader>();
    public string ZipRoot { get => zipRoot; set { if (Set(ref zipRoot, value)) InvalidatePack(); } }
    public ObservableCollection<string> ZipRoots { get; } = [];
    public string ZipLayout => zipPreview is { IsMrpack: false } p ? TextCatalog.Format("ZipLayout", p.Plan.ContentRoot.Length == 0 ? "/" : p.Plan.ContentRoot, p.Plan.Files.Sum(f => f.Size) / 1048576.0) : "";
    private string packArchive = "", packName = "", packHint = "PackHint";
    private bool packConsent, packPins, packStale;
    private int packPending;
    private double packProgress;
    private IReadOnlyList<string> packFindings = [];
    public ObservableCollection<PackOptionalChoice> PackOptionalFiles { get; } = [];
    public ObservableCollection<string> PackFiles { get; } = [];
    public ObservableCollection<string> PackBlockers { get; } = [];
    public string PackArchive => packArchive;
    public string PackName { get => packName; set { if (Set(ref packName, value)) InvalidatePack(); } }
    public bool PackPins { get => packPins; set { if (Set(ref packPins, value)) InvalidatePack(); } }
    public bool PackConsent { get => packConsent; set { if (Set(ref packConsent, value)) Changed(nameof(CanImportPack)); } }
    public bool CanImportPack => !IsBusy && !IsGameLaunchActive && packPreview is { CanImport: true } && !packStale && PackConsent && packPending == 0;
    public double PackProgress { get => packProgress; private set => Set(ref packProgress, value); }
    public string PackHint => TextCatalog.Get(packHint);
    public string PackPending => TextCatalog.Format("PackPending", packPending);
    public string PackSummary => packPreview is { } p ? TextCatalog.Format("PackSummary", p.Name, p.Version, p.Minecraft, p.Loader, p.LoaderVersion ?? "—", p.Files.Count, p.DownloadBytes / 1048576.0) : "";
    private void InvalidatePack() { packStale = true; PackConsent = false; RefreshPackText(); }
    private void RefreshPackText()
    {
        PackFiles.Clear(); PackBlockers.Clear();
        if (packPreview is { } p)
        {
            foreach (var f in p.Entries)
            {
                var final = p.Files.FirstOrDefault(x => x.Path == f.Path);
                var state = f.Selected ? final?.Source != f.Source && f.Source == "download" ? "PackReplaced" : "PackSelected" : "PackExcluded";
                PackFiles.Add(TextCatalog.Format("PackFileLine", f.Path, f.Source, TextCatalog.Get("PackSide." + f.Client), TextCatalog.Get("PackSide." + f.Server), TextCatalog.Get(state), TextCatalog.Get(f.Cached ? "PackCached" : "PackMissing"), f.Size / 1024.0));
            }
            foreach (var problem in p.Blockers) PackBlockers.Add(TextCatalog.Get(problem.Code) + ": " + problem.Detail);
            foreach (var path in p.ManualFiles) PackFiles.Add(TextCatalog.Format("ExportManual", path));
        }
        foreach (var finding in packFindings) PackBlockers.Add(finding);
        if (zipPreview is { IsMrpack: false } z)
        {
            foreach (var path in z.Plan.Excluded) PackFiles.Add(TextCatalog.Format("ZipExcluded", path));
            foreach (var warning in z.Review?.Warnings ?? []) PackBlockers.Add(warning);
        }
        if (instancePreview is { } i)
        {
            foreach (var warning in i.Warnings) { var parts = warning.Split(": ", 2); PackBlockers.Add(TextCatalog.Get(parts[0]) + (parts.Length == 2 ? ": " + parts[1] : "")); }
            foreach (var warning in i.Review?.Warnings ?? []) PackBlockers.Add(warning);
        }
        foreach (var property in new[] { nameof(PackSummary), nameof(PackHint), nameof(PackPending), nameof(CanImportPack), nameof(PackArchive), nameof(IsZipPack), nameof(ZipLayout), nameof(IsInstancePack), nameof(InstanceLayout), nameof(IsMinecraftPack), nameof(MinecraftImportContent), nameof(MinecraftImportRequiredContent), nameof(IsModrinthAppPack), nameof(ModrinthAppContent), nameof(IsCurseForgeAppPack) }) Changed(property);
    }
    private async Task RefreshPackPendingAsync(CancellationToken ct)
    {
        try { packPending = (await services.Modpacks.PendingAsync(ct)).Count; if (packPending > 0) packHint = "PackRecovery"; }
        catch (Exception e) when (e is not OperationCanceledException) { services.Log.Error("modpack.recovery", e); packPending = 1; packHint = "PackRecovery"; }
        RefreshPackText();
    }
    public async Task ChoosePackAsync(string archive)
    {
        if (IsBusy || IsGameLaunchActive) return;
        ResetModrinthAppImport();
        minecraftMode=false; minecraftPreview=null;
        instanceMode = false; instancePreview = null;
        packArchive = archive; packName = ""; zipMode = false; zipPreview = null; PackOptionalFiles.Clear(); packPreview = null; packFindings = []; PackConsent = false; PackProgress = 0;
        await PreviewPackAsync();
    }
    public async Task ChooseZipPackAsync(string archive)
    {
        if (IsBusy || IsGameLaunchActive) return;
        ResetModrinthAppImport();
        minecraftMode=false; minecraftPreview=null;
        instanceMode = false; instancePreview = null;
        packArchive = archive; packName = ""; zipMode = true; zipPreview = null; zipRoot = ""; ZipRoots.Clear();
        PackOptionalFiles.Clear(); packPreview = null; packFindings = []; PackConsent = false; PackProgress = 0;
        await PreviewPackAsync();
    }
    public async Task ChooseInstancePackAsync(string source, bool curseForgeApp = false)
    {
        if (IsBusy || IsGameLaunchActive) return;
        ResetModrinthAppImport();
        minecraftMode=false; minecraftPreview=null;
        curseForgeAppMode = curseForgeApp;
        instanceMode = true; instancePreview = null; zipMode = false; zipPreview = null;
        packArchive = source; packName = ""; PackOptionalFiles.Clear(); packPreview = null; packFindings = []; PackConsent = false; PackProgress = 0;
        await PreviewPackAsync();
    }
    public async Task ChooseMinecraftPackAsync(string profilesFile)
    {
        if(IsBusy||IsGameLaunchActive)return;
        ResetModrinthAppImport();
        minecraftMode=true; minecraftPreview=null; minecraftProfileKey="";minecraftContent="";MinecraftProfileChoices.Clear();
        instanceMode=false;instancePreview=null;zipMode=false;zipPreview=null;
        packArchive=profilesFile;packName="";PackOptionalFiles.Clear();packPreview=null;packFindings=[];PackConsent=false;PackProgress=0;
        await PreviewPackAsync();
    }
    public void SetMinecraftImportContent(string folder)
    { if(IsBusy||IsGameLaunchActive)return;minecraftContent=folder;Changed(nameof(MinecraftImportContent));InvalidatePack(); }
    private void ResetModrinthAppImport()
    {
        modrinthAppMode = false;
        modrinthAppPreview = null;
        modrinthInstanceId = "";
        modrinthProfilesDirectory = null;
        ModrinthAppChoices.Clear();
        Changed(nameof(SelectedModrinthAppProfile));
    }
    public async Task ChooseModrinthAppAsync(string database)
    {
        if (IsBusy || IsGameLaunchActive) return;
        ResetModrinthAppImport();
        modrinthAppMode = true;
        minecraftMode = instanceMode = zipMode = false;
        minecraftPreview = null;
        instancePreview = null;
        zipPreview = null;
        packArchive = database;
        packName = "";
        PackOptionalFiles.Clear();
        packPreview = null;
        packFindings = [];
        PackConsent = false;
        PackProgress = 0;
        await PreviewPackAsync();
    }
    public async Task ChooseModrinthProfilesDirectoryAsync(string directory)
    {
        if (IsBusy || IsGameLaunchActive || !modrinthAppMode) return;
        modrinthProfilesDirectory = directory;
        InvalidatePack();
        await PreviewPackAsync();
    }
    public async Task PreviewPackAsync()
    {
        if (IsGameLaunchActive) return;
        await RunAsync(async ct =>
        {
            packStale = true; PackConsent = false; packFindings = [];
            MrpackPreview preview;
            if (modrinthAppMode)
            {
                modrinthAppPreview = await services.Modpacks.ModrinthAppReader.PreviewAsync(packArchive,
                    new(modrinthInstanceId, packName, PackPins, modrinthProfilesDirectory), ct);
                if (!ModrinthAppChoices.SequenceEqual(modrinthAppPreview.Choices))
                {
                    var id = modrinthInstanceId;
                    ModrinthAppChoices.Clear();
                    foreach (var choice in modrinthAppPreview.Choices) ModrinthAppChoices.Add(choice);
                    modrinthInstanceId = ModrinthAppChoices.Any(choice => choice.Id == id) ? id : "";
                    Changed(nameof(SelectedModrinthAppProfile));
                }
                preview = modrinthAppPreview.Plan;
                instancePreview = modrinthAppPreview.Snapshot;
            }
            else if (minecraftMode)
            {
                minecraftPreview=await services.Modpacks.MinecraftReader.PreviewAsync(packArchive,new(minecraftProfileKey,packName,minecraftContent.Length==0?null:minecraftContent,PackPins),ct);
                MinecraftProfileChoices.Clear();foreach(var choice in minecraftPreview.Choices)MinecraftProfileChoices.Add(choice);Changed(nameof(SelectedMinecraftImportProfile));
                preview=minecraftPreview.Plan;instancePreview=minecraftPreview.Snapshot;
            }
            else if (instanceMode)
            {
                instancePreview = curseForgeAppMode
                    ? await services.Modpacks.CurseForgeAppReader.PreviewAsync(packArchive, new(packName, PackPins), ct)
                    : await services.Modpacks.InstanceReader.PreviewAsync(packArchive, new(packName, PackPins), ct);
                preview = instancePreview.Plan;
            }
            else if (zipMode)
            {
                zipPreview = await services.Modpacks.ZipReader.PreviewAsync(packArchive, new(packName, ZipMinecraft, ZipLoader, ZipLoaderVersion, ZipRoots.Count > 0 ? ZipRoot == "/" ? "" : ZipRoot : null, PackPins), ct);
                preview = zipPreview.Plan;
                if (zipPreview.IsMrpack) { zipMode = false; zipPreview = null; }
                else
                {
                    ZipRoots.Clear(); foreach (var root in zipPreview.Roots) ZipRoots.Add(root.Length == 0 ? "/" : root);
                    zipRoot = zipPreview.Request.ContentRoot is { } r ? r.Length == 0 ? "/" : r : ""; Changed(nameof(ZipRoot));
                }
            }
            else preview = await services.Modpacks.Reader.PreviewAsync(packArchive, packName, PackOptionalFiles.Where(p => p.Selected).Select(p => p.Path).ToArray(), PackPins, ct);
            packPreview = preview; packName = (minecraftMode && minecraftProfileKey.Length == 0 || modrinthAppMode && modrinthInstanceId.Length == 0) ? "" : preview.ProfileName; Changed(nameof(PackName)); packStale = false; packHint = "PackHint";
            var selected = PackOptionalFiles.Where(x => x.Selected).Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
            PackOptionalFiles.Clear();
            foreach (var f in preview.Entries.Where(e => e.Optional))
            { var item = new PackOptionalChoice(f.Path, InvalidatePack) { Selected = selected.Contains(f.Path) }; PackOptionalFiles.Add(item); }
            packStale = false; RefreshPackText(); Status = TextCatalog.Get(preview.CanImport ? "PackPreviewReady" : "PackBlocked");
        });
        RefreshPackText();
    }
    public async Task ImportPackAsync()
    {
        if (!CanImportPack) return; var preview = packPreview!;
        await RunAsync(async ct =>
        {
            packHint = "PackImporting"; RefreshPackText();
            var result = await TrackDownloadsAsync(TextCatalog.Get("InstallPackUi"), preview.ProfileName, async progress =>
                modrinthAppMode && modrinthAppPreview is not null ? await services.Modpacks.ImportModrinthAppAsync(modrinthAppPreview, PackConsent, progress, ct) :
                minecraftMode && minecraftPreview is not null ? await services.Modpacks.ImportMinecraftAsync(minecraftPreview, PackConsent, progress, ct) :
                instanceMode && instancePreview is not null ? await (curseForgeAppMode
                    ? services.Modpacks.ImportCurseForgeAppAsync(instancePreview, PackConsent, progress, ct)
                    : services.Modpacks.ImportInstanceAsync(instancePreview, PackConsent, progress, ct)) :
                zipMode && zipPreview is not null ? await services.Modpacks.ImportZipAsync(zipPreview, PackConsent, progress, ct) :
                await services.Modpacks.ImportAsync(preview, PackConsent, progress, ct), ct,
                p => PackProgress = p.Total > 0 ? Math.Clamp(100.0 * p.Received / p.Total.Value, 0, 99) : 0);
            await ReloadProfilesAsync(ct); SelectedProfile = Profiles.Single(p => p.Id == result.Profile.Id);
            PackProgress = 100; packHint = "PackImported"; PackConsent = false; packStale = true;
            packFindings = result.Receipt.Review?.Warnings ?? [];
            Status = TextCatalog.Get("PackImported");
        });
        if (!LastOperationSucceeded) { PackConsent = false; packStale = true; packHint = "PackRecovery"; }
        await RefreshPackPendingAsync(CancellationToken.None); RefreshPackText();
    }
    internal async Task CreateReconciledCopyAsync(PackCopyPlan plan)
    {
        if (IsBusy || IsGameLaunchActive) return;
        await RunAsync(async ct =>
        {
            var result = await services.Modpacks.CreateCopyAsync(plan, true, ct: ct);
            await ReloadProfilesAsync(CancellationToken.None);
            SelectedProfile = Profiles.Single(p => p.Id == result.Profile.Id);
            Status = TextCatalog.Get("PackImported");
        });
        await RefreshPackPendingAsync(CancellationToken.None);
    }

    public async Task RecoverPacksAsync()
    {
        if (IsBusy || IsGameLaunchActive || !PackConsent) return;
        await RunAsync(async ct => {
            await services.Modpacks.RecoverAsync(true, ct); await ReloadProfilesAsync(ct);
            PackConsent = false; await RefreshPackPendingAsync(ct); packHint = "PackRecovered"; RefreshPackText(); Status = TextCatalog.Get("PackRecovered");
        });
        if (!LastOperationSucceeded) { PackConsent = false; await RefreshPackPendingAsync(CancellationToken.None); }
        RefreshPackText();
    }
}
