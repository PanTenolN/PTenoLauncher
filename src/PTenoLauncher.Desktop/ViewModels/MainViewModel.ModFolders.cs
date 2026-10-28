using System.Diagnostics;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public string GetModsFolderPath()
    {
        var profile=RequireProfile();
        if(profile.Loader==ModLoader.Vanilla)throw new InvalidOperationException("Vanilla не завантажує моди.");
        return SafePath.Join(services.Paths.Game(profile.Id),"mods");
    }
    public void OpenModsFolder()
    {
        try
        {
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Відкриття теки через Провідник підтримується у Windows.");
            var folder=GetModsFolderPath();Directory.CreateDirectory(folder);SafePath.RejectLinks(folder);
            var explorer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
            var start=new ProcessStartInfo(explorer){UseShellExecute=false};start.ArgumentList.Add(folder);
            using var process=Process.Start(start);
            Status="Відкрито теку модів: "+folder;
        }
        catch(Exception ex){services.Log.Error("mods.open-folder",ex);Status=ex.Message;}
    }
}
