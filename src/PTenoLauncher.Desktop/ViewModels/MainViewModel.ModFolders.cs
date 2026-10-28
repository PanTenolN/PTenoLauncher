using PTenoLauncher.Desktop.Localization;
using System.Diagnostics;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public string GetModsFolderPath()
    {
        var profile=RequireProfile();
        if(profile.Loader==ModLoader.Vanilla)throw new InvalidOperationException(TextCatalog.Get("Message039"));
        return SafePath.Join(services.Paths.Game(profile.Id),"mods");
    }
    public void OpenModsFolder()
    {
        try
        {
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException(TextCatalog.Get("Message040"));
            var folder=GetModsFolderPath();Directory.CreateDirectory(folder);SafePath.RejectLinks(folder);
            var explorer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
            var start=new ProcessStartInfo(explorer){UseShellExecute=false};start.ArgumentList.Add(folder);
            using var process=Process.Start(start);
            Status=TextCatalog.Get("Message041")+folder;
        }
        catch(Exception ex){services.Log.Error("mods.open-folder",ex);Status=ex.Message;}
    }
}
