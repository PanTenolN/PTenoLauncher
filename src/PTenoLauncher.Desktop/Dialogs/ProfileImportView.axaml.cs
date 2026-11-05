using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed partial class ProfileImportView : UserControl
{
    public event Action? Imported;
    public ProfileImportView() => InitializeComponent();
    private async void ComparePacks(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsIdle: true } vm && TopLevel.GetTopLevel(this) is Window owner)
            await new PackCompareDialog { DataContext = vm }.ShowDialog(owner);
    }
    private async void ChooseMrpack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("PackChoose"), AllowMultiple = false, FileTypeFilter = [new("Modrinth .mrpack") { Patterns = ["*.mrpack"] }] });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string file) await vm.ChoosePackAsync(file);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void PreviewMrpack(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewPackAsync(); }
    private async void ChooseZipPack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("ZipChoose"), AllowMultiple = false, FileTypeFilter = [new("ZIP") { Patterns = ["*.zip", "*.mrpack"] }] });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string file) await vm.ChooseZipPackAsync(file);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ChooseInstanceDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new() { Title = TextCatalog.Get("InstanceChooseDirectory"), AllowMultiple = false });
            if (folders.Count == 1 && folders[0].TryGetLocalPath() is string folder) await vm.ChooseInstancePackAsync(folder);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ChooseInstanceZip(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("InstanceChooseZip"), AllowMultiple = false, FileTypeFilter = [new("Instance ZIP") { Patterns = ["*.zip"] }] });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string file) await vm.ChooseInstancePackAsync(file);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ImportMrpack(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { CanImportPack: true } vm) { await vm.ImportPackAsync(); if (vm.LastOperationSucceeded && vm.SelectedProfile is not null) Imported?.Invoke(); } }
    private async void ChooseMinecraftLauncher(object? sender, RoutedEventArgs e)
    {
        if(DataContext is not MainViewModel{IsBusy:false}vm)return;
        try{var files=await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new(){Title=TextCatalog.Get("MinecraftImportChooseFile"),AllowMultiple=false,FileTypeFilter=[new("Minecraft Launcher profiles"){Patterns=["launcher_profiles.json","launcher_profiles_microsoft_store.json"]}]});if(files.Count==1&&files[0].TryGetLocalPath() is string file)await vm.ChooseMinecraftPackAsync(file);}
        catch(Exception ex){vm.Status=ex.Message;}
    }
    private async void ChooseMinecraftImportContent(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainViewModel{IsBusy:false}vm)return;
        try{var folders=await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new(){Title=TextCatalog.Get("MinecraftImportChooseContent"),AllowMultiple=false});if(folders.Count==1&&folders[0].TryGetLocalPath() is string folder){vm.SetMinecraftImportContent(folder);await vm.PreviewPackAsync();}}
        catch(Exception ex){vm.Status=ex.Message;}
    }
    private async void RecoverMrpacks(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.RecoverPacksAsync(); }
    private async void ChooseModrinthApp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = TextCatalog.Get("ModrinthAppDatabase"), AllowMultiple = false,
                FileTypeFilter = [new("Modrinth App") { Patterns = ["app.db"] }]
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string file) await vm.ChooseModrinthAppAsync(file);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ChooseModrinthProfilesDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new()
            { Title = TextCatalog.Get("ModrinthAppProfilesFolder"), AllowMultiple = false });
            if (folders.Count == 1 && folders[0].TryGetLocalPath() is string folder) await vm.ChooseModrinthProfilesDirectoryAsync(folder);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ChooseCurseForgeApp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new()
            { Title = TextCatalog.Get("CurseForgeAppDirectory"), AllowMultiple = false });
            if (folders.Count == 1 && folders[0].TryGetLocalPath() is string folder)
                await vm.ChooseInstancePackAsync(folder, curseForgeApp: true);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
}
