using Avalonia.Controls;
using PTenoLauncher.Desktop.Localization;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Core;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow : Window
{
    private MainViewModel? updateVm;
    private LauncherUpdateDialog? updateDialog;
    public MainWindow()
    {
        InitializeComponent();
        ModDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ModDropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        ProfileModDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ProfileModDropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) =>
        {
            if (updateVm is not null) { updateVm.LauncherUpdateOffered -= OfferLauncherUpdate; updateVm.LauncherRestartReady -= RestartLauncher; updateVm.ProfileConsoleUpdated -= ScrollProfileConsole; }
            updateVm = DataContext as MainViewModel;
            if (updateVm is not null) { updateVm.LauncherUpdateOffered += OfferLauncherUpdate; updateVm.LauncherRestartReady += RestartLauncher; updateVm.ProfileConsoleUpdated += ScrollProfileConsole; }
        };
        Closed += (_, _) => updateVm?.CancelLauncherUpdateCheck();
        Closing += (_, e) =>
        {
            if(DataContext is not MainViewModel {TranslationDirty:true} vm)return;
            e.Cancel=true;vm.Status=TextCatalog.Get("AiUxUnsaved");vm.Page=LauncherPage.Settings;
            this.FindControl<TabControl>("SettingsTabs")!.SelectedItem=this.FindControl<TabItem>("MyTranslationsTab");
            this.FindControl<Button>("SaveTranslationEdits")?.Focus();
        };
    }
    private async void OfferLauncherUpdate(object? sender, EventArgs e)
    {
        if (updateVm is null || updateDialog is not null || !IsVisible) return;
        updateDialog = new(updateVm);
        try { await updateDialog.ShowDialog(this); } finally { updateDialog = null; }
    }
    private void RestartLauncher(object? sender, EventArgs e) { updateDialog?.Close(); Close(); }
    private async void CheckLauncherUpdates(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.CheckLauncherUpdatesAsync(); }
    private async void SaveLauncherUpdateSettings(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveLauncherUpdateSettingsAsync(); }
    public Task<bool> EnsureLanguageSelectedAsync()
    {
        if (DataContext is not MainViewModel { IsInitialized: true } vm) return Task.FromResult(false);
        return vm.NeedsLanguageSelection
            ? new LanguageSelectionDialog(vm).ShowDialog<bool>(this)
            : Task.FromResult(true);
    }
    private async void RememberFabricVersion(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is PTenoLauncher.Infrastructure.Minecraft.FabricLoaderVersion version &&
            DataContext is MainViewModel { IsIdle: true } vm)
            await vm.RememberSelectedFabricVersionAsync(version);
    }
    private async void SaveLibraryLayout(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not CheckBox check) return;
        vm.LibraryGrid = check.IsChecked == true;
        try { await vm.SaveLibraryLayoutAsync(); }
        catch (Exception ex) { vm.Services.Log.Error("library.layout", ex); }
    }

    private void SelectLibraryProfile(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel { IsProfiles: true } vm && sender is ListBox { SelectedItem: GameProfile profile })
            vm.SelectedProfile = profile;
    }

    private void ClearLibraryLoader(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.LibraryLoader = null;
    }

    private async void Navigate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Button { Tag: string tag } && Enum.TryParse<LauncherPage>(tag, out var page))
        {
            vm.Page = page;
            if (page == LauncherPage.Downloads) await vm.RefreshDownloadHistoryAsync();
            if(page==LauncherPage.Settings && this.FindControl<TabControl>("SettingsTabs")?.SelectedItem is TabItem{Name:"MyTranslationsTab"} && vm.HasProfile && !vm.IsBusy && !vm.TranslationDirty)await vm.LoadTranslationDraftsAsync();
        }
    }
    private async void CreateProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        var previous = vm.SelectedProfile;
        await vm.NewCommand.ExecuteAsync();
        if (!vm.LastOperationSucceeded || vm.SelectedProfile is not null) return;
        var saved = await new ProfileEditor { DataContext = vm }.ShowDialog<bool>(this);
        if (saved && vm.SelectedProfile is { } created) vm.OpenProfileWorkspace(created.Id, previous?.Id);
        if (!saved && vm.SelectedProfile is null) vm.SelectedProfile = previous;
    }
    private void EditProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedProfile: { } profile } vm) vm.OpenProfileWorkspace(profile.Id);
    }
    private void BackFromWorkspace(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.BackFromProfileWorkspace();
    }
    private async void EditWorkspaceSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy || vm.SelectedProfile is null) return;
        await new ProfileEditor { DataContext = vm }.ShowDialog<bool>(this);
    }
    private async void SelectProfileSection(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Button { Tag: string tag } && Enum.TryParse<ProfileSection>(tag, out var section))
        {
            vm.ChooseWorkspaceSection(section);
            if (section == ProfileSection.Console) ScrollProfileConsole();
            if (section == ProfileSection.Mods && vm.WorkspaceModTableVisible) await vm.InspectWorkspaceModsAsync();
            if (vm.WorkspaceContentVisible) await vm.RefreshWorkspaceContentAsync();
        }
    }
    private async void OpenModCatalog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { CanUseOnlineCatalog: true, SelectedProfile: { } profile } vm)
        {
            await ShowModCatalogAsync(vm, profile);
        }
    }
    private void SortWorkspaceMods(object? sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm) vm.SortWorkspaceMods(); }
    private async void OpenCart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsIdle: true, CanManageMods: true, SelectedProfile: { } profile } vm)
        {
            await ShowModCatalogAsync(vm, profile, review: true);
        }
    }
    internal async Task OpenLocalCartAsync(MainViewModel vm, Guid profileId)
    {
        if (vm.SelectedProfile is { } profile && profile.Id == profileId && vm.Cart.Count > 0)
        {
            var find = await new LocalDependenciesDialog().ShowDialog<bool?>(this);
            if (find is null || vm.SelectedProfile?.Id != profileId) return;
            vm.SelectLocalImportMode(!find.Value);
            await ShowModCatalogAsync(vm, profile, review: true);
        }
    }
    private async Task ShowModCatalogAsync(MainViewModel vm, GameProfile profile, bool review = false)
    {
        var installed = await new ModCatalogDialog(vm, profile, review).ShowDialog<bool>(this);
        if (!installed || vm.SelectedProfile?.Id != profile.Id) return;
        if (vm.WorkspaceProfileId != profile.Id) vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Mods);
        await vm.InspectWorkspaceModsAsync();
    }
    private void OpenWorkspaceFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Button { Tag: string kind }) vm.OpenWorkspaceFolder(kind);
    }
    private async void ExportProfilePack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false, SelectedProfile: { } profile } vm ||
            vm.WorkspaceProfileId != profile.Id) return;
        if (await new PackExportDialog(vm, profile).ShowDialog<string?>(this) is not null)
            vm.Status = TextCatalog.Get("ExportDone");
    }
    private void ClearProfileConsole(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ClearProfileConsole();
    }
    private async void CopyProfileConsole(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && Clipboard is not null)
            await Clipboard.SetTextAsync(string.Join(Environment.NewLine, vm.VisibleConsoleLines));
    }
    private async void CopyFullProfileConsole(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || Clipboard is null) return;
        try
        {
            if (await vm.ReadFullConsoleAsync() is { } text) await Clipboard.SetTextAsync(text);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { vm.Status = error.Message; }
    }
    private async void NavigateProfileConsole(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Button { Tag: string direction }) return;
        try
        {
            if (direction == "live") vm.ShowLiveConsole();
            else await vm.ShowConsoleHistoryAsync(direction == "previous" ? -1 : direction == "next" ? 1 : 0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { vm.Status = error.Message; }
    }
    private void ScrollProfileConsole()
    {
        if (DataContext is not MainViewModel { WorkspaceConsole: true, ConsoleAutoScroll: true, ConsoleShowingHistory: false } vm || vm.VisibleConsoleRows.Count == 0) return;
        this.FindControl<ListBox>("ProfileConsoleList")?.ScrollIntoView(vm.VisibleConsoleRows.Count - 1);
    }
    private void OpenProfileFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.OpenProfileFolder();
    }
    private async void DeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedProfile: not null, IsBusy: false } vm) await new DeleteProfileDialog(vm).ShowDialog<bool>(this);
    }
    private async void AutoJava(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PrepareJavaAsync(); }
    private async void StopGame(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.GameLaunchId is not Guid launchId) return;
        var confirmed = await new StopGameDialog().ShowDialog<bool>(this);
        await vm.StopGameAsync(launchId, confirmed);
    }
    private async void Unarchive(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.UnarchiveAsync(); }
    private async void ThemeChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && DataContext is MainViewModel { IsBusy: false, IsInitialized: true } vm) await vm.SaveThemeAsync(); }
    private async void LanguageChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && DataContext is MainViewModel { IsBusy: false, IsInitialized: true, NeedsLanguageSelection: false } vm) await vm.SaveLanguageAsync(); }
    private async void AccountChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && DataContext is MainViewModel { IsBusy: false, IsInitialized: true, SelectedAccount: not null } vm) await vm.SelectAccountAsync(); }
    private async void AiChanged(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsInitialized: true } vm) await vm.SaveAiAsync(); }
    private async void ApplyTheme(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveThemeAsync(); }
    private void ClearCart(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsBusy: false } vm) { vm.ClearInstallationCart(); } }
    private async void RestoreBackup(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("BackupPicker"), AllowMultiple = false, FileTypeFilter = [new(TextCatalog.Get("BackupFile")) { Patterns = ["*.zip"] }] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is string path) await vm.RestoreBackupAsync(path);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ImportMods(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        var profileId = vm.SelectedProfile?.Id;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("ModsPicker"), AllowMultiple = true, FileTypeFilter = [new(TextCatalog.Get("Mods")) { Patterns = ["*.jar"] }] });
            if (files.Count == 0 || profileId is null || vm.SelectedProfile?.Id != profileId) return;
            await vm.AddModsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
            if (!vm.LastOperationSucceeded) return;
            await OpenLocalCartAsync(vm, profileId.Value);
            if (sender is Button { Name: "SettingsImportModsButton" } && vm.CanManageMods) vm.Page = LauncherPage.Mods;
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is not null && vm.SelectedProfile is { } profile)
        {
            await vm.AddModsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>().Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)));
            if (vm.LastOperationSucceeded) await OpenLocalCartAsync(vm, profile.Id);
        }
        e.Handled = true;
    }
    private async void ApplyOptionalSelection(object? sender, RoutedEventArgs e) { if(DataContext is MainViewModel vm)await vm.ResolveCartAsync(); }
    private async void ClearOptionalSelection(object? sender, RoutedEventArgs e) { if(DataContext is MainViewModel vm)await vm.ClearOptionalAsync(); }
    private async void ResolveModCart(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.ResolveCartAsync(); }
    private async void SearchModrinth(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SearchRemoteAsync(); }
    private async void CheckModUpdates(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.CheckModUpdatesAsync(); }
    private async void PreviewModUpdates(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewModUpdatesAsync(); }
    private async void ApplyModUpdates(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.ApplyModUpdatesAsync(); }
    private async void PinModVersion(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SetModPinAsync(true); }
    private async void UnpinModVersion(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SetModPinAsync(false); }
    private async void LoadModUpdateHistory(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.RefreshModUpdateHistoryAsync(); }
    private async void PreviewModRollback(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewModRollbackAsync(); }
    private async void RecoverModUpdates(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.RecoverModUpdatesAsync(); }
    private async void LoadModVersions(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadRemoteVersionsAsync(); }
    private async void InstallRemoteMods(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.InstallRemoteAsync(); }
    private void ClearRemoteCart(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsBusy: false } vm) vm.ClearInstallationCart(); }
    private void OpenModsFolder(object? sender,RoutedEventArgs e) { if(DataContext is MainViewModel {CanManageMods:true} vm)vm.OpenModsFolder(); }
    private async void ReviewInstalledMods(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { CanManageMods: true, IsBusy: false } vm) await new ModBaselineDialog(vm).ShowDialog<bool>(this);
    }
    private async void LoadQuiltVersions(object? sender,RoutedEventArgs e) { if(DataContext is MainViewModel vm)await vm.LoadQuiltVersionsAsync(); }
    private async void LoadFabricVersions(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadFabricVersionsAsync(); }
    private async void LoadForgeVersions(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadForgeVersionsAsync(); }
    private async void PrepareForgeComponents(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PrepareForgeComponentsAsync(); }
    private async void SaveAiConfiguration(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveAiConfigurationAsync(); }
    private async void DeleteAiKey(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.DeleteAiKeyAsync(); }
    private async void AskAi(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.AskAiAsync(vm.AiLogMode); }
    private async void CheckAiConnection(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.CheckAiConnectionAsync(); }
    private async void SaveAiModel(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveAiModelAsync(); }
    private void ReplaceAiKey(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { vm.ReplaceAiKey(); this.FindControl<TextBox>("AiKeyInput")?.Focus(); } }
    private void ClearAiConversation(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsBusy: false } vm) vm.ClearAiConversation(); }
    private async void CopyAiAnswer(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm && Clipboard is not null) await Clipboard.SetTextAsync(vm.AiResponse); }
    private void AiExampleOne(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.AiInput = TextCatalog.Get("AiUxExampleOne"); }
    private void AiExampleTwo(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.AiInput = TextCatalog.Get("AiUxExampleTwo"); }
    private static void OpenOfficial(string url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    private void OpenApiKeys(object? sender, RoutedEventArgs e) => OpenOfficial("https://platform.openai.com/api-keys");
    private void OpenApiBilling(object? sender, RoutedEventArgs e) => OpenOfficial("https://platform.openai.com/settings/organization/billing/overview");
    private async void OnReadTranslationResources(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.ReadTranslationResourcesAsync(); }
    private async void CreateLocalTranslationProfile(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.CreateTranslationProfileAsync(); }
    private void SelectTrialSample(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsBusy: false } vm) vm.SelectTranslationTrialSample(); }
    private async void PreviewTrial(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewTranslationTrialAsync(); }
    private async void RunTrial(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.TranslateTrialAsync(); }
    private async void PreviewTranslationDelete(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewDeleteTranslationAsync(); }
    private async void OnDeleteTranslationEntry(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.DeleteTranslationAsync(); }
    private async void ImportClientPackMetadata(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false, HasProfile: true } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("AiUxClientMetadata"), AllowMultiple = false, FileTypeFilter = [new("Minecraft client JAR") { Patterns = ["*.jar"] }] });
        if (files.Count == 1 && files[0].TryGetLocalPath() is string jar) await vm.ImportTranslationClientMetadataAsync(jar);
    }
    private async void PreviewTranslationTransfer(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewTranslationTransferAsync(); }
    private async void PreviewAllTranslations(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PreviewTranslationTransferAsync(true); }
    private async void DiscardTranslationEdits(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.DiscardTranslationEditsAsync(); }
    private async void OpenTranslationDraft(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source,sender) || sender is not ListBox list || DataContext is not MainViewModel { IsBusy: false, TranslationListRebuilding: false } vm || list.SelectedItem is not PTenoLauncher.Infrastructure.AI.TranslationPreview selected) return;
        if(vm.TranslationDirty) { list.SelectedItem=vm.SelectedTranslationDraft; return; }
        if(Equals(vm.SelectedTranslationDraft,selected))return;
        vm.SelectedTranslationDraft=selected;await vm.LoadTranslationEditorAsync();
    }
    private async void TranslationProfileChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && ReferenceEquals(e.Source, sender) && this.FindControl<TabControl>("SettingsTabs")?.SelectedItem is TabItem{Name:"MyTranslationsTab"} && DataContext is MainViewModel { Page: LauncherPage.Settings, IsBusy: false, HasProfile: true } vm && !vm.TranslationDirty) await vm.LoadTranslationDraftsAsync(); }
    private async void SettingsTabChanged(object? sender, SelectionChangedEventArgs e)
    { if (ReferenceEquals(e.Source, sender) && sender is TabControl { SelectedItem: TabItem { Name: "MyTranslationsTab" } } && DataContext is MainViewModel { Page: LauncherPage.Settings, IsBusy: false, HasProfile: true } vm && !vm.TranslationDirty) await vm.LoadTranslationDraftsAsync(); }
    private void OpenTranslationsFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { HasProfile: true } vm) return;
        var folder = vm.TranslationFolder; PTenoLauncher.Infrastructure.Storage.SafePath.RejectLinks(folder); System.IO.Directory.CreateDirectory(folder);
        var start = new System.Diagnostics.ProcessStartInfo("explorer.exe"); start.ArgumentList.Add(folder); System.Diagnostics.Process.Start(start);
    }
    private void OpenGeneratedPackFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || string.IsNullOrEmpty(vm.TranslationPackPath)) return;
        var folder = System.IO.Path.GetDirectoryName(vm.TranslationPackPath)!; PTenoLauncher.Infrastructure.Storage.SafePath.RejectLinks(folder);
        var start = new System.Diagnostics.ProcessStartInfo("explorer.exe"); start.ArgumentList.Add(folder); System.Diagnostics.Process.Start(start);
    }
    private async void AnalyzeAiLog(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.AskAiAsync(true); }
    private async void LoadTranslationDrafts(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadTranslationDraftsAsync(); }
    private async void TranslateMods(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.TranslateModsAsync(); }
    private async void LoadTranslationEditor(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadTranslationEditorAsync(); }
    private async void SaveTranslationEditor(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveTranslationEditorAsync(); }
    private async void ExportTranslationPack(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.ExportTranslationPackAsync(); }
    private async void ImportTranslationJars(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = TextCatalog.Get("AiImport"), AllowMultiple = true, FileTypeFilter = [new("JAR") { Patterns = ["*.jar"] }] });
            if (files.Count > 0) await vm.ImportTranslationJarsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void TranslateAllMods(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.TranslateModsAsync(true); }
}
