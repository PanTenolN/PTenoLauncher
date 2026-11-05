using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class TranslationToolsUiTests
{
    private static async Task Idle(MainViewModel vm) { for (int i=0;vm.IsBusy && i<600;i++) await Task.Delay(10);Assert.False(vm.IsBusy); }
    [AvaloniaFact] public async Task HiddenRowsKeepEditsSelectionAndFlagsAcrossSearchAndDirectionFiltersOffline()
    {
        using var f = new AiUxFixture(); var p = await TranslationToolsTests.Profile(f); await f.Translations.ImportAsync(p,[TranslationToolsTests.Jar(f,300)],"uk_ua",sourceLocale:"fr_fr");
        using var services = new LauncherServices(f.Workspace.Paths,f.Transport(),()=>throw new Exception("No key allowed"));var vm = new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;
        var w=new MainWindow{DataContext=vm,Width=980,Height=680};w.Show();try
        {
            vm.Page=LauncherPage.Settings;w.FindControl<TabControl>("SettingsTabs")!.SelectedItem=w.FindControl<TabItem>("MyTranslationsTab");await Idle(vm);
            var selected=vm.SelectedTranslationDraft;var row=vm.TranslationRows[0];row.Translation="Manual %s";row.TrialSelected=true;
            w.FindControl<TextBox>("TranslationRowSearch")!.Text="no matching rows";Assert.Empty(vm.VisibleTranslationRows);Assert.True(vm.TranslationDirty);Assert.Same(selected,vm.SelectedTranslationDraft);
            vm.TranslationSearch="hidden library";Assert.Empty(vm.VisibleTranslationDrafts);Assert.Same(selected,vm.SelectedTranslationDraft);Assert.Equal("Manual %s",row.Translation);
            vm.TranslationRowSearch="Manual";Assert.Same(row,Assert.Single(vm.VisibleTranslationRows));Assert.True(row.TrialSelected);
            vm.TranslationRowSearch="key.0299";Assert.Equal("key.0299",Assert.Single(vm.VisibleTranslationRows).Key);
            vm.TranslationRowSearch="Original 299";Assert.Single(vm.VisibleTranslationRows);vm.TranslationRowSearch="";vm.TranslationSearch="";
            w.FindControl<Button>("SaveTranslationEdits")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.False(vm.TranslationDirty);
            vm.TranslationRowManual=true;Assert.Equal("Manual %s",Assert.Single(vm.VisibleTranslationRows).Translation);vm.TranslationLibraryManual=true;Assert.Single(vm.VisibleTranslationDrafts);
            vm.TranslationLibraryReview=true;Assert.Empty(vm.VisibleTranslationDrafts);Assert.NotNull(vm.SelectedTranslationDraft);Assert.Equal(0,f.Calls);
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task SmallTrialUsesActualControlsAndCannotAccidentallyBecomeFullTranslation()
    {
        using var f=new AiUxFixture();await TranslationToolsTests.Profile(f);using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>"synthetic-api-key-only");var vm=new MainViewModel(services);await vm.InitializeAsync();vm.AiEnabled=true;vm.AiKey="synthetic-api-key-only";vm.AiModel="fixture-model";await vm.SaveAiConfigurationAsync();
        var w=new MainWindow{DataContext=vm};w.Show();try
        {
            await vm.ImportTranslationJarsAsync([TranslationToolsTests.Jar(f)]);vm.SelectedSourceLanguage=vm.SourceLanguages.Single(x=>x.Code=="fr_fr");await vm.ReadTranslationResourcesAsync();
            f.Respond=async(r,ct)=>{using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));using var input=JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);Assert.Equal(5,input.RootElement.EnumerateObject().Count());Assert.False(input.RootElement.TryGetProperty("key.0005",out _));return AiUxFixture.Completed("{\"translations\":{\"key.0000\":\"Trial 0 %s\",\"key.0001\":\"Trial 1 %s\",\"key.0002\":\"Trial 2 %s\",\"key.0003\":\"Trial 3 %s\",\"key.0004\":\"Trial 4 %s\"}}");};
            w.FindControl<Button>("SelectTranslationSample")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(5,vm.TranslationRows.Count(r=>r.TrialSelected));
            w.FindControl<Button>("PreviewTranslationTrial")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.Contains("key.0004",vm.TranslationTransferPreview);Assert.DoesNotContain("key.0005",vm.TranslationTransferPreview);Assert.Equal(0,f.Calls);
            vm.TranslationConsent=true;vm.TranslationRows[0].TrialSelected=false;Assert.False(vm.TranslationConsent);await vm.TranslateTrialAsync();Assert.Equal(0,f.Calls);
            vm.SelectTranslationTrialSample();await vm.PreviewTranslationTrialAsync();vm.TranslationConsent=true;
            w.FindControl<Button>("TranslateTrial")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Equal(1,f.Calls);Assert.Equal(7,vm.SelectedTranslationDraft!.Missing);
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task OfflineProfileEditorExportAndConfirmedDeleteNeedNoAccountOrGameInstall()
    {
        using var f=new AiUxFixture();using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>throw new Exception("No key allowed"));var vm=new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;
        var w=new MainWindow{DataContext=vm};w.Show();try
        {
            w.FindControl<TextBox>("TranslationProfileName")!.Text="Manual fixture";w.FindControl<TextBox>("TranslationMinecraft")!.Text="1.21";
            w.FindControl<Button>("CreateTranslationProfile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Single(vm.Profiles);Assert.Empty(services.Authentication.GetAccounts());
            await vm.ImportTranslationJarsAsync([f.Jar()]);vm.SelectedSourceLanguage=Assert.Single(vm.SourceLanguages);await vm.ReadTranslationResourcesAsync();vm.TranslationRows[0].Translation="Manual %s";await vm.SaveTranslationEditorAsync();await vm.ExportTranslationPackAsync();Assert.True(vm.LastOperationSucceeded,vm.Status);var pack=vm.TranslationPackPath;Assert.True(File.Exists(pack));Assert.False(File.Exists(services.Paths.Receipt(vm.SelectedProfile!.Id)));
            w.FindControl<Button>("PreviewDeleteTranslation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.Contains("demo",vm.TranslationDeleteText);Assert.Contains(pack,vm.TranslationDeleteText);
            await vm.DeleteTranslationAsync();Assert.False(vm.LastOperationSucceeded);Assert.Single(vm.TranslationDrafts);
            w.FindControl<CheckBox>("ConfirmTranslationDelete")!.IsChecked=true;w.FindControl<Button>("DeleteTranslationEntry")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Empty(vm.TranslationDrafts);Assert.True(File.Exists(pack));Assert.False(vm.AiEnabled);Assert.Equal(0,f.Calls);
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task TranslationControlsRemainLocalizableFocusableAndScrollableWithLongRows()
    {
        using var f=new AiUxFixture();var p=await TranslationToolsTests.Profile(f);await f.Translations.ImportAsync(p,[TranslationToolsTests.Jar(f,1200)],"uk_ua",sourceLocale:"fr_fr");
        using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>throw new Exception("No key allowed"));var vm=new MainViewModel(services);await vm.InitializeAsync();var w=new MainWindow{DataContext=vm,Width=980,Height=680};w.Show();try
        {
            vm.Page=LauncherPage.Settings;w.FindControl<TabControl>("SettingsTabs")!.SelectedItem=w.FindControl<TabItem>("MyTranslationsTab");await Idle(vm);
            foreach(var language in TextCatalog.Languages){vm.SelectedLanguage=language;await Idle(vm);TextCatalog.ValidateCatalog(language.Code,TextCatalog.Catalog(language.Code));Assert.Equal(TextCatalog.Get("AiUxPreviewTrial"),w.FindControl<Button>("PreviewTranslationTrial")!.Content);}
            Assert.Equal(1200,vm.TranslationRows.Count);vm.TranslationRows[0].Translation=new string('a',12000)+" %s";Assert.True(vm.TranslationDirty);vm.TranslationRowSearch="key.1199";Assert.Single(vm.VisibleTranslationRows);Assert.True(vm.TranslationDirty);
            var table=w.FindControl<ListBox>("TranslationTable")!;Assert.Equal(360,table.MaxHeight);Assert.True(w.FindControl<TextBox>("TranslationRowSearch")!.Focusable);Assert.True(w.FindControl<Button>("CreateTranslationPack")!.Focusable);Assert.Equal(0,f.Calls);
            await vm.DiscardTranslationEditsAsync();
        }finally{w.DataContext=null;w.Close();TextCatalog.Apply("uk");}
    }
}
