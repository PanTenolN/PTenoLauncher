using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public ObservableCollection<QuiltLoaderVersion> QuiltVersions {get;}=[];
    private QuiltLoaderVersion? selectedQuiltVersion;
    public QuiltLoaderVersion? SelectedQuiltVersion {get=>selectedQuiltVersion;set=>Set(ref selectedQuiltVersion,value);}
    private const string DefaultQuiltStatus="Без вибору використовується встановлена або найновіша релізна версія Quilt. Метадані модів Quilt ще потребують підтримки.";
    private string quiltStatus=DefaultQuiltStatus;
    public string QuiltStatus {get=>quiltStatus;set=>Set(ref quiltStatus,value);}
    public bool IsQuiltProfile=>SelectedProfile is {Loader:ModLoader.Quilt};
    private void ResetQuiltSelection(){QuiltVersions.Clear();SelectedQuiltVersion=null;QuiltStatus=DefaultQuiltStatus;}
    public Task LoadQuiltVersionsAsync()=>RunAsync(async ct=>
    {
        var profile=RequireProfile();if(profile.Loader!=ModLoader.Quilt)throw new InvalidOperationException("Виберіть профіль Quilt.");
        var versions=await services.Quilt.GetVersionsAsync(profile.MinecraftVersion,ct);
        InstallationReceipt? receipt=null;
        try{receipt=await AtomicFile.ReadJsonAsync<InstallationReceipt>(services.Paths.Receipt(profile.Id),ct);}
        catch(System.Text.Json.JsonException ex){services.Log.Error("quilt.receipt",ex);}
        if(SelectedProfile?.Id!=profile.Id)return;
        QuiltVersions.Clear();foreach(var version in versions)QuiltVersions.Add(version);
        SelectedQuiltVersion=QuiltVersions.FirstOrDefault(v=>v.Version==(receipt?.Loader==ModLoader.Quilt?receipt.LoaderVersion:null))??QuiltVersions.FirstOrDefault(v=>!v.IsPrerelease);
        QuiltStatus="Версій Quilt: "+QuiltVersions.Count+(services.Quilt.UsedCachedMetadata?". Використано кеш метаданих.":".");
        Status="Виберіть версію Quilt і натисніть «Встановити / відновити».";
    });
}
