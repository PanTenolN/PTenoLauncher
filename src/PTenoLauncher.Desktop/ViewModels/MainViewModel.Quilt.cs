using PTenoLauncher.Desktop.Localization;
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
    private static string DefaultQuiltStatus => TextCatalog.Get("Message063");
    private string quiltStatus=DefaultQuiltStatus;
    public string QuiltStatus {get=>TextCatalog.Relocalize(quiltStatus);set=>Set(ref quiltStatus,value);}
    public bool IsQuiltProfile=>SelectedProfile is {Loader:ModLoader.Quilt};
    private void ResetQuiltSelection(){QuiltVersions.Clear();SelectedQuiltVersion=null;QuiltStatus=DefaultQuiltStatus;}
    public Task LoadQuiltVersionsAsync()=>RunAsync(async ct=>
    {
        var profile=RequireProfile();if(profile.Loader!=ModLoader.Quilt)throw new InvalidOperationException(TextCatalog.Get("Message064"));
        var versions=await services.Quilt.GetVersionsAsync(profile.MinecraftVersion,ct);
        InstallationReceipt? receipt=null;
        try{receipt=await AtomicFile.ReadJsonAsync<InstallationReceipt>(services.Paths.Receipt(profile.Id),ct);}
        catch(System.Text.Json.JsonException ex){services.Log.Error("quilt.receipt",ex);}
        if(SelectedProfile?.Id!=profile.Id)return;
        QuiltVersions.Clear();foreach(var version in versions)QuiltVersions.Add(version);
        SelectedQuiltVersion=QuiltVersions.FirstOrDefault(v=>v.Version==(receipt?.Loader==ModLoader.Quilt?receipt.LoaderVersion:null))??QuiltVersions.FirstOrDefault(v=>!v.IsPrerelease);
        QuiltStatus=TextCatalog.Get("Message065")+QuiltVersions.Count+(services.Quilt.UsedCachedMetadata?TextCatalog.Get("Message066"):".");
        Status=TextCatalog.Get("Message067");
    });
}
