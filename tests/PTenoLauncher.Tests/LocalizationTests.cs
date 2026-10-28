using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Authentication;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class LocalizationTests
{
    [AvaloniaFact]
    public async Task LanguageChangesActualEditorAndOfflineOperationWithoutNetworkAndPersists()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm };
        var editor = new ProfileEditor { DataContext = vm };
        window.Show(); editor.Show(window);
        try
        {
            vm.Offline = true;
            vm.SelectedLanguage = TextCatalog.Languages.Single(x => x.Code == "en");
            Assert.Contains("OFFLINE", vm.ModeText);
            Assert.Contains("Snapshots", editor.GetVisualDescendants().OfType<CheckBox>().Select(x => x.Content));
            // The actual ComboBox change handler starts an asynchronous SQLite save.
            for (var attempt = 0; vm.IsBusy && attempt < 500; attempt++) await Task.Delay(10);
            Assert.False(vm.IsBusy);
            await vm.SaveLanguageAsync();
            Assert.Equal("en", await services.Store.GetSettingAsync("ui.language"));
            vm.Memory = "invalid";
            await vm.SaveCommand.ExecuteAsync();
            Assert.Equal("Memory must be a whole number of MB.", vm.Status);
            vm.SelectedLanguage = TextCatalog.Languages.Single(x => x.Code == "uk");
            Assert.Contains("ОФЛАЙН", vm.ModeText);
            Assert.Equal("RAM має бути цілим числом MB.", vm.Status);
            Assert.Contains("Снапшоти", editor.GetVisualDescendants().OfType<CheckBox>().Select(x => x.Content));
            for (var attempt = 0; vm.IsBusy && attempt < 500; attempt++) await Task.Delay(10);
            await services.Store.SetSettingAsync("ui.language", "en");
            var restored = new MainViewModel(services);
            await restored.InitializeAsync();
            Assert.Equal("en", restored.SelectedLanguage.Code);
            Assert.Contains("Local data loaded", restored.Status);
            restored.ProfileName = "Synthetic language profile"; restored.VersionId = "1.21.1";
            await restored.SaveCommand.ExecuteAsync();
            Assert.True(restored.LastOperationSucceeded, restored.Status);
            await restored.CheckCommand.ExecuteAsync();
            Assert.Contains("Minecraft is not installed", restored.Readiness);
            Assert.Contains("Sign in online first", restored.Readiness);
        }
        finally { editor.Close(); window.Close(); TextCatalog.Apply("uk"); }
    }

    [Theory]
    [InlineData("https://user.auth.xboxlive.com/user/authenticate", AuthenticationStage.XboxUser)]
    [InlineData("https://xsts.auth.xboxlive.com/xsts/authorize", AuthenticationStage.Xsts)]
    [InlineData("https://api.minecraftservices.com/authentication/login_with_xbox?secret=private", AuthenticationStage.MinecraftLogin)]
    [InlineData("https://api.minecraftservices.com/entitlements/mcstore", AuthenticationStage.Entitlements)]
    [InlineData("https://api.minecraftservices.com/minecraft/profile", AuthenticationStage.MinecraftProfile)]
    [InlineData("https://example.com/private/path", AuthenticationStage.Unknown)]
    public void AuthenticationStageUsesFixedClassificationWithoutPrivatePathOrQuery(string address, AuthenticationStage expected)
    {
        var uri = new Uri(address);
        var stage = AuthenticationServiceException.IdentifyStage(uri);
        Assert.Equal(expected, stage);
        var error = new AuthenticationServiceException(uri.Host, System.Net.HttpStatusCode.Forbidden, stage);
        Assert.Contains("stage=" + expected, error.SafeDiagnostic);
        Assert.DoesNotContain("secret", error.SafeDiagnostic);
        Assert.DoesNotContain("private", error.SafeDiagnostic);
        Assert.DoesNotContain("?", error.SafeDiagnostic);
    }
}
