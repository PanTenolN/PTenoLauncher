using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class AdditionalLanguagesTests
{
    [Theory]
    [InlineData("uk")]
    [InlineData("en")]
    [InlineData("pl")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    public void EmbeddedCatalogHasAllKeysAndPreservesFormats(string language)
    {
        var catalog = TextCatalog.Catalog(language);
        Assert.True(catalog.Count >= 201);
        TextCatalog.ValidateCatalog(language, catalog);
        Assert.Equal(TextCatalog.Keys.Order(), catalog.Keys.Order());
    }

    [Fact]
    public void IncompleteOrBrokenTranslationIsRejected()
    {
        var catalog = TextCatalog.Catalog("de").ToDictionary();
        catalog.Remove("DeleteWarning");
        Assert.Throws<InvalidDataException>(() => TextCatalog.ValidateCatalog("de", catalog));
        catalog = TextCatalog.Catalog("de").ToDictionary();
        catalog["GameInstalled"] = "Minecraft {0}: {1}."; // Missing Java requirement argument.
        Assert.Throws<InvalidDataException>(() => TextCatalog.ValidateCatalog("de", catalog));
        catalog["GameInstalled"] = "Minecraft {0}: {1}, Java {2";
        Assert.Throws<InvalidDataException>(() => TextCatalog.ValidateCatalog("de", catalog));
    }

    [AvaloniaTheory]
    [InlineData("uk", "Головна", "Зберегти")]
    [InlineData("en", "Home", "Save")]
    [InlineData("pl", "Strona główna", "Zapisz")]
    [InlineData("de", "Startseite", "Speichern")]
    [InlineData("es", "Inicio", "Guardar")]
    [InlineData("fr", "Accueil", "Enregistrer")]
    [InlineData("pt-BR", "Início", "Salvar")]
    [InlineData("ru", "Главная", "Сохранить")]
    public async Task ActualPickerSwitchesAndPersistsLanguageWithoutTranslatingUserData(string language, string home, string save)
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            vm.Page = LauncherPage.Settings;
            var picker = window.FindControl<ComboBox>("LanguagePicker")!;
            Assert.NotNull(picker);
            Assert.Equal(8, picker.ItemCount);
            picker.SelectedItem = TextCatalog.Languages.Single(x => x.Code == language);
            Assert.Equal(language, TextCatalog.Language);
            Assert.Equal(home, TextCatalog.Get("Home"));
            Assert.Equal(save, TextCatalog.Get("Save"));
            vm.ConsoleLines.Add("Synthetic original log: Привіт / Hello / {token-redacted}");
            for (var attempt = 0; vm.IsBusy && attempt < 500; attempt++) await Task.Delay(10);
            Assert.False(vm.IsBusy);
            await vm.SaveLanguageAsync();
            Assert.Equal(language, await services.Store.GetSettingAsync("ui.language"));
            var restored = new MainViewModel(services); await restored.InitializeAsync();
            Assert.Equal(language, restored.SelectedLanguage.Code);
            Assert.Equal("Synthetic original log: Привіт / Hello / {token-redacted}", Assert.Single(vm.ConsoleLines));
            var ready = TextCatalog.RenderReadiness(new ReadinessReport([], 7, 2));
            Assert.Contains("7", ready); Assert.Contains("2", ready);
            Assert.DoesNotContain("ReadinessReady", ready);
            TextCatalog.Apply("unknown-locale");
            Assert.Equal("uk", TextCatalog.Language);
        }
        finally { window.Close(); TextCatalog.Apply("uk"); }
    }
}
