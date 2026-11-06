using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed class LoaderLabelConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is PTenoLauncher.Core.ModLoader.Vanilla ? TextCatalog.Get("VanillaLoaderLabel") : value?.ToString();
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
public sealed partial class ProfileEditor : Window
{
    private readonly CancellationTokenSource refreshCancellation = new();
    private MainViewModel? editor;
    private bool importing;
    private (string Minecraft, PTenoLauncher.Core.ModLoader Loader)? lastCatalogTarget;
    public ProfileEditor()
    {
        InitializeComponent();
        this.FindControl<ProfileImportView>("NewProfileImport")!.Imported += () => Close(true);
        Opened += async (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            editor = vm;
            vm.SetProfileValidation(false);
            vm.PropertyChanged += EditorChanged;
            if (vm.IsCreatingProfile)
            {
                Title = TextCatalog.Get("CreateProfile");
                this.FindControl<TextBlock>("EditorTitle")!.Text = Title;
                this.FindControl<Button>("SaveProfileButton")!.Content = Title;
                vm.ShowReleases = true; vm.ShowSnapshots = false; vm.ShowHistorical = false;
                vm.PackConsent = false;
            }
            if (vm.SelectedProfile is { } profile) { vm.VersionId = profile.MinecraftVersion; vm.Loader = profile.Loader; }
            vm.ResetEditorLoaderChoice();
            vm.VersionSearch = "";
            await vm.RefreshCatalogIfStaleAsync(refreshCancellation.Token);
            EditorChanged(vm, new(nameof(MainViewModel.IsIdle)));
        };
        Closing += (_, args) => { if (editor?.IsBusy == true && (importing || editor.JavaPreparing)) args.Cancel = true; };
        Closed += (_, _) =>
        {
            refreshCancellation.Cancel();
            if (editor is not null) { editor.PropertyChanged -= EditorChanged; editor.PackConsent = false; }
        };
    }
    private void EditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.VersionId) or nameof(MainViewModel.Loader))
            lastCatalogTarget = null;
        if (args.PropertyName is nameof(MainViewModel.VersionId) or nameof(MainViewModel.Loader) or nameof(MainViewModel.IsIdle))
            Dispatcher.UIThread.Post(async () =>
            {
                if (refreshCancellation.IsCancellationRequested || importing || editor is not { IsIdle: true, HasEditorLoader: true, HasVersionChoice: true } vm) return;
                var target = (vm.VersionId, vm.Loader);
                if (lastCatalogTarget == target) return;
                lastCatalogTarget = target;
                await vm.LoadEditorLoaderVersionsAsync(refreshCancellation.Token);
            });
    }
    private void ChooseManualMode(object? sender, RoutedEventArgs e) => SetMode(false);
    private void ChooseImportMode(object? sender, RoutedEventArgs e) => SetMode(true);
    private void SetMode(bool import)
    {
        if (editor is not { IsCreatingProfile: true, IsIdle: true }) return;
        importing = import;
        this.FindControl<Button>("NewManualMode")!.Classes.Set("primary", !import);
        this.FindControl<Button>("NewImportMode")!.Classes.Set("primary", import);
        editor.PackConsent = false;
        this.FindControl<ProfileImportView>("NewProfileImport")!.IsVisible = import;
        this.FindControl<ScrollViewer>("ManualEditor")!.IsVisible = !import;
        this.FindControl<StackPanel>("ManualName")!.IsVisible = !import;
        this.FindControl<TextBlock>("ImportedName")!.IsVisible = import;
        this.FindControl<TextBlock>("DraftSummary")!.IsVisible = !import;
        this.FindControl<TextBlock>("ImportStatus")!.IsVisible = import;
        this.FindControl<Panel>("ManualHint")!.IsVisible = !import;
        this.FindControl<Button>("SaveProfileButton")!.IsVisible = !import;
        if (!import) EditorChanged(editor, new(nameof(MainViewModel.IsIdle)));
    }
    private async void FiltersChanged(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveVersionVisibilityAsync(); }
    private async void LoadLoaderVersions(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            lastCatalogTarget = (vm.VersionId, vm.Loader);
            await vm.LoadEditorLoaderVersionsAsync(refreshCancellation.Token);
        }
    }
    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        vm.SetProfileValidation(true);
        if (!vm.ProfileFieldsComplete)
        {
            var first = vm.MissingProfileName ? "ProfileNameInput" : vm.MissingProfileMinecraft ? "VersionSearchInput" :
                vm.MissingProfileMemory ? "ProfileMemoryInput" : "ProfileLoaderVersionList";
            this.FindControl<Control>(first)!.Focus();
            return;
        }
        await vm.SaveCommand.ExecuteAsync(); if (vm.LastOperationSucceeded) Close(true);
    }
    private async void Cancel(object? sender, RoutedEventArgs e)
    {
        if (editor is { JavaPreparing: true }) await editor.CancelCommand.ExecuteAsync();
        else Close(false);
    }
    private async void BrowseJava(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        await vm.RunAsync(async _ =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Java x64", FileTypeFilter = [new("Java") { Patterns = ["java.exe"] }] });
            if (files.Count > 0) vm.JavaPath = files[0].TryGetLocalPath() ?? "";
        });
    }
}
