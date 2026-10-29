using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class LanguageOnboardingTests
{
    [AvaloniaTheory]
    [InlineData("uk")]
    [InlineData("en")]
    [InlineData("pl")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    public async Task FirstRunShowsEightLanguagesPreviewsWithoutSavingAndRestoresConfirmedChoice(string language)
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        await services.Store.SetSettingAsync("network.offline", "True");
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var owner = new MainWindow { DataContext = vm }; owner.Show();
        try
        {
            Assert.True(vm.IsInitialized, vm.Status); Assert.True(vm.NeedsLanguageSelection); Assert.True(vm.Offline);
            var startup = owner.EnsureLanguageSelectedAsync();
            var dialog = Assert.Single(owner.OwnedWindows.OfType<LanguageSelectionDialog>());
            var picker = dialog.FindControl<ListBox>("FirstRunLanguagePicker")!;
            Assert.Equal(8, picker.ItemCount);
            Assert.Equal(TextCatalog.Languages.Select(l => l.Name), picker.Items.Cast<UiLanguage>().Select(l => l.Name));
            picker.SelectedItem = TextCatalog.Languages.Single(l => l.Code == language);
            Assert.Equal(language, TextCatalog.Language);
            Assert.Equal(TextCatalog.Get("FirstRunTitle"), dialog.FindControl<TextBlock>("LanguageSelectionTitle")!.Text);
            Assert.True(vm.NeedsLanguageSelection);
            // The hidden Settings picker must not auto-save a welcome-menu preview.
            Assert.Null(await services.Store.GetSettingAsync("ui.language"));
            dialog.FindControl<Button>("ContinueLanguageSelectionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await startup.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(vm.NeedsLanguageSelection);
            Assert.Equal(language, await services.Store.GetSettingAsync("ui.language"));

            var restored = new MainViewModel(services); await restored.InitializeAsync();
            owner.DataContext = restored;
            Assert.False(restored.NeedsLanguageSelection); Assert.Equal(language, restored.SelectedLanguage.Code);
            Assert.True(await owner.EnsureLanguageSelectedAsync()); Assert.Empty(owner.OwnedWindows);
            Assert.False(restored.AiEnabled); Assert.Empty(restored.Accounts);
        }
        finally { owner.Close(); TextCatalog.Apply("uk"); }
    }

    [AvaloniaFact]
    public async Task ExitDoesNotSaveAPreviewAndNextStartupStillRequestsLanguage()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var owner = new MainWindow { DataContext = vm }; owner.Show();
        try
        {
            var startup = owner.EnsureLanguageSelectedAsync();
            var dialog = Assert.Single(owner.OwnedWindows.OfType<LanguageSelectionDialog>());
            dialog.FindControl<ListBox>("FirstRunLanguagePicker")!.SelectedItem = TextCatalog.Languages.Single(l => l.Code == "de");
            dialog.FindControl<Button>("ExitLanguageSelectionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await startup); Assert.Null(await services.Store.GetSettingAsync("ui.language"));
            var retry = new MainViewModel(services); await retry.InitializeAsync();
            Assert.True(retry.NeedsLanguageSelection); Assert.Equal("uk", retry.SelectedLanguage.Code);
        }
        finally { owner.Close(); TextCatalog.Apply("uk"); }
    }

    [AvaloniaTheory]
    [InlineData("xx-unsupported", true, "uk")]
    [InlineData("", true, "uk")]
    [InlineData("EN", false, "en")]
    [InlineData("pt-br", false, "pt-BR")]
    public async Task ExistingProfilesSurviveAndOnlyMissingOrUnsupportedPreferencesRequestOnboarding(string saved, bool needsSelection, string canonical)
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync(); var profile = ws.Profile(); await services.Store.SaveProfileAsync(profile);
        var sentinel = Path.Combine(ws.Paths.Game(profile.Id), "saves", "synthetic-language-check.txt");
        await File.WriteAllTextAsync(sentinel, "SYNTHETIC - NOT A MINECRAFT WORLD");
        await services.Store.SetSettingAsync("ui.language", saved);
        await services.Store.SetSettingAsync("onboarding.sentinel", "preserved");
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        Assert.Equal(needsSelection, vm.NeedsLanguageSelection); Assert.Equal(canonical, vm.SelectedLanguage.Code);
        Assert.Equal(profile.Id, Assert.Single(vm.Profiles).Id);
        Assert.Equal("preserved", await services.Store.GetSettingAsync("onboarding.sentinel"));
        Assert.Equal("SYNTHETIC - NOT A MINECRAFT WORLD", await File.ReadAllTextAsync(sentinel));
        TextCatalog.Apply("uk");
    }

    [AvaloniaFact]
    public async Task SaveFailureKeepsDialogOpenAndAllowsRetryWithoutAcceptingTheChoice()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var owner = new MainWindow { DataContext = vm }; owner.Show();
        var backup = ws.Paths.Database + ".onboarding-test-backup";
        try
        {
            var startup = owner.EnsureLanguageSelectedAsync();
            var dialog = Assert.Single(owner.OwnedWindows.OfType<LanguageSelectionDialog>());
            dialog.FindControl<ListBox>("FirstRunLanguagePicker")!.SelectedItem = TextCatalog.Languages.Single(l => l.Code == "en");
            SqliteConnection.ClearAllPools(); File.Move(ws.Paths.Database, backup); Directory.CreateDirectory(ws.Paths.Database);
            dialog.FindControl<Button>("ContinueLanguageSelectionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; vm.IsBusy && attempt < 500; attempt++) await Task.Delay(10);
            Assert.False(vm.IsBusy); Assert.False(vm.LastOperationSucceeded);
            Assert.False(startup.IsCompleted); Assert.True(vm.NeedsLanguageSelection);
            Assert.True(dialog.FindControl<TextBlock>("LanguageSaveError")!.IsVisible);
            Directory.Delete(ws.Paths.Database); File.Move(backup, ws.Paths.Database);
            dialog.FindControl<Button>("ContinueLanguageSelectionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await startup.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("en", await services.Store.GetSettingAsync("ui.language"));
        }
        finally
        {
            if (Directory.Exists(ws.Paths.Database)) Directory.Delete(ws.Paths.Database);
            if (File.Exists(backup)) File.Move(backup, ws.Paths.Database, true);
            owner.Close(); TextCatalog.Apply("uk");
        }
    }
}
