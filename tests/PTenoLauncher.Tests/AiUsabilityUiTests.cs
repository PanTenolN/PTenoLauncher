using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class AiUsabilityUiTests
{
    [AvaloniaFact] public async Task VisibleComposerCancelsActualPendingRequestWithoutSavingConversation()
    {
        using var f=new AiUxFixture();var entered=new TaskCompletionSource();f.Respond=async(_,ct)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,ct);return AiUxFixture.Completed("never");};
        using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>"synthetic-api-key-only");var vm=new MainViewModel(services);await vm.InitializeAsync();vm.AiEnabled=true;vm.AiKey="synthetic-local-key";vm.AiModel="fixture-model";await vm.SaveAiConfigurationAsync();
        var w=new MainWindow{DataContext=vm,Width=980,Height=680};w.Show();try
        {
            vm.Page=LauncherPage.Settings;w.FindControl<TabControl>("SettingsTabs")!.SelectedItem=w.FindControl<TabItem>("AiAssistantTab");w.UpdateLayout();
            Assert.Equal(TextCatalog.Get("AiInDevelopment"), w.FindControl<TextBlock>("AiAssistantDevelopmentLabel")!.Text);
            var send=w.FindControl<Button>("AskAiButton")!;var point=send.TranslatePoint(new Point(0,send.Bounds.Height),w);Assert.NotNull(point);Assert.True(point.Value.Y<630,$"Composer must be visible without scrolling at minimum window size. Bottom={point.Value.Y}, window={w.Bounds}, button={send.Bounds}");
            w.FindControl<TextBox>("AiQuestionInput")!.Text="Question";w.FindControl<CheckBox>("AiRequestConsent")!.IsChecked=true;send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await entered.Task;
            Assert.True(vm.AiWaiting);Assert.True(w.FindControl<TextBlock>("AiWaitingText")!.IsVisible);w.FindControl<Button>("CancelAiRequest")!.Command!.Execute(null);await Idle(vm);
            Assert.False(vm.AiWaiting);Assert.Single(vm.AiConversation);Assert.DoesNotContain(vm.AiConversation,m=>m.Role=="assistant");Assert.Equal(1,f.Calls);Assert.Empty(Directory.GetFiles(f.Workspace.Paths.Root,"*conversation*",SearchOption.AllDirectories));
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task UnchangedReviewedRowCanBeAcceptedOfflineAndReopenedWithoutJar()
    {
        using var f=new AiUxFixture(); await f.Workspace.Store.InitializeAsync(); var p=f.Workspace.Profile() with {MinecraftVersion="1.21"}; await f.Workspace.Store.SaveProfileAsync(p);
        await f.Translations.ImportAsync(p,[f.Jar()],"uk_ua",sourceLocale:"fr_fr");
        await f.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","Keep %s"}},sourceLocale:"fr_fr");
        await f.Translations.ImportAsync(p,[f.Jar(text:"Changed French %s")],"uk_ua",sourceLocale:"fr_fr");
        using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>throw new Exception("No key allowed"));var vm=new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;
        var w=new MainWindow{DataContext=vm};w.Show();try
        {
            vm.Page=LauncherPage.Settings;w.FindControl<TabControl>("SettingsTabs")!.SelectedItem=w.FindControl<TabItem>("MyTranslationsTab");await Idle(vm);w.UpdateLayout();
            Assert.Equal(TextCatalog.Get("AiInDevelopment"), w.FindControl<TextBlock>("TranslationsDevelopmentLabel")!.Text);
            Assert.Equal(1,Assert.Single(vm.TranslationDrafts).Review);Assert.Equal("Keep %s",Assert.Single(vm.TranslationRows).Translation);
            var accept=w.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Content,TextCatalog.Get("AiUxAcceptRow")));accept.IsChecked=true;Assert.True(vm.TranslationDirty);
            w.FindControl<Button>("SaveTranslationEdits")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Equal(0,Assert.Single(vm.TranslationDrafts).Review);
            Assert.Equal("Keep %s",Assert.Single(vm.TranslationRows).Translation);Assert.False(vm.TranslationDirty);Assert.Equal(0,f.Calls);
            var restarted=new MainViewModel(services);await restarted.InitializeAsync();Assert.Equal("Keep %s",Assert.Single(restarted.TranslationRows).Translation);Assert.Empty(restarted.SourceLanguages);
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task CatalogForbiddenKeepsManualModelUsableAndDoesNotGrantGenerationProof()
    {
        using var f=new AiUxFixture();f.Respond=(r,_)=>Task.FromResult(r.Method==HttpMethod.Get?new HttpResponseMessage(HttpStatusCode.Forbidden):AiUxFixture.Completed("Fixture reply"));
        using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>"synthetic-api-key-only");var vm=new MainViewModel(services);await vm.InitializeAsync();vm.AiEnabled=true;vm.AiKey="synthetic-local-key";vm.AiModel="fixture-model";await vm.SaveAiConfigurationAsync();
        vm.AiConnectionConsent=true;await vm.CheckAiConnectionAsync();Assert.False(vm.LastOperationSucceeded);Assert.Equal(TextCatalog.Get("AiUxCatalogDenied"),vm.Status);Assert.Equal("fixture-model",vm.AiModel);Assert.False(vm.AiConnectionConsent);
        vm.AiInput="Question";vm.AiConsent=true;await vm.AskAiAsync(false);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Equal(TextCatalog.Get("AiUxResponsesChecked"),vm.AiConnectionState);Assert.Equal(2,f.Calls);
        vm.AiModel="different-fixture";Assert.NotEqual(TextCatalog.Get("AiUxResponsesChecked"),vm.AiConnectionState);await vm.SaveAiModelAsync();Assert.Equal("different-fixture",await services.Store.GetSettingAsync("ai.openai.model"));Assert.Equal(2,f.Calls);
    }
    private static async Task Idle(MainViewModel vm) { for(int i=0;i<600 && vm.IsBusy;i++) await Task.Delay(10); Assert.False(vm.IsBusy); }
    [AvaloniaFact] public async Task KeyCardMakesNoRequestsOnInitializeSaveReplaceOrDeleteAndManualCheckNeedsConsent()
    {
        using var f=new AiUxFixture(); using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>{f.KeyReads++;return "synthetic-api-key-only";}); var vm=new MainViewModel(services); await vm.InitializeAsync();
        var w=new MainWindow{DataContext=vm};w.Show();try
        {
            Assert.Equal(0,f.Calls);Assert.Equal(0,f.KeyReads);Assert.False(vm.AiHasKey);Assert.NotEqual('\0',w.FindControl<TextBox>("AiKeyInput")!.PasswordChar);
            vm.AiEnabled=true; w.FindControl<TextBox>("AiKeyInput")!.Text="synthetic-local-key-only";w.FindControl<Button>("SaveAiKeyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);
            Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.True(vm.AiHasKey);Assert.Empty(vm.AiKey);Assert.Equal(0,f.Calls);Assert.Equal(0,f.KeyReads);Assert.Equal(TextCatalog.Get("AiUxKeySaved"),w.FindControl<TextBlock>("AiSavedKeyState")!.Text);
            w.FindControl<Button>("CheckAiConnectionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.Equal(0,f.Calls);Assert.Equal(0,f.KeyReads);
            w.FindControl<CheckBox>("AiConnectionConsent")!.IsChecked=true;w.FindControl<Button>("CheckAiConnectionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Equal(1,f.Calls);Assert.Equal(1,f.KeyReads);Assert.Equal("fixture-model",Assert.Single(vm.AiModels));
            vm.ReplaceAiKey();Assert.Equal(1,f.Calls);await vm.DeleteAiKeyAsync();Assert.False(vm.AiHasKey);Assert.Equal(1,f.Calls);vm.AiEnabled=false;Assert.Equal(TextCatalog.Get("AiUxDisabled"),vm.AiConnectionState);
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task ActualChatControlsSendRealBoundedSessionContextAndClearWithoutPersistence()
    {
        using var f=new AiUxFixture();var requestSizes=new List<int>();f.Respond=async(r,ct)=>{using var d=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));requestSizes.Add(d.RootElement.GetProperty("input").GetArrayLength());Assert.DoesNotContain("person@example.com",await r.Content.ReadAsStringAsync(ct));return AiUxFixture.Completed("Fixture answer only");};
        using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>"synthetic-api-key-only");var vm=new MainViewModel(services);await vm.InitializeAsync();vm.AiEnabled=true;vm.AiKey="synthetic-local-key-only";vm.AiModel="fixture-model";await vm.SaveAiConfigurationAsync();
        var w=new MainWindow{DataContext=vm};w.Show();try
        {
            for(int i=0;i<8;i++)
            {
                w.FindControl<TextBox>("AiQuestionInput")!.Text="Question "+i+" person@example.com";Assert.False(vm.AiConsent);Assert.DoesNotContain("person@example.com",w.FindControl<TextBox>("AiRequestPreview")!.Text!);
                w.FindControl<CheckBox>("AiRequestConsent")!.IsChecked=true;Assert.True(w.FindControl<Button>("AskAiButton")!.IsEnabled);w.FindControl<Button>("AskAiButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);
            }
            Assert.Equal(1,requestSizes[0]);Assert.Equal(3,requestSizes[1]);Assert.Equal(13,requestSizes[^1]);Assert.Equal(16,vm.AiConversation.Count);Assert.False(vm.AiConsent);Assert.Empty(vm.AiInput);
            w.FindControl<Button>("CopyAiAnswerButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Yield();Assert.Equal(vm.AiResponse,await w.Clipboard!.TryGetTextAsync());
            w.FindControl<Button>("ClearAiChatButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Empty(vm.AiConversation);Assert.True(vm.AiChatEmpty);Assert.Equal(8,f.Calls);
            Assert.Empty(Directory.GetFiles(f.Workspace.Paths.Root,"*conversation*",SearchOption.AllDirectories));
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task NonEnglishSourceLibraryTableAndAutomaticPackWorkOfflineWithoutAiKey()
    {
        using var f=new AiUxFixture();using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>{f.KeyReads++;throw new InvalidOperationException("No key read allowed");});await services.Store.InitializeAsync();var profile=f.Workspace.Profile() with {MinecraftVersion="1.21"};await services.Store.SaveProfileAsync(profile);var vm=new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;
        var w=new MainWindow{DataContext=vm};w.Show();try
        {
            await vm.ImportTranslationJarsAsync([f.Jar()]);Assert.Equal("fr_fr",Assert.Single(vm.SourceLanguages).Code);Assert.Null(vm.SelectedSourceLanguage);
            w.FindControl<ComboBox>("TranslationSource")!.SelectedItem=Assert.Single(vm.SourceLanguages);w.FindControl<ComboBox>("TranslationTarget")!.SelectedItem=MainViewModel.TranslationLanguages.Single(l=>l.Code=="uk_ua");
            w.FindControl<Button>("ReadTranslationResources")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Single(vm.TranslationDrafts);Assert.Equal("Bonjour %s",Assert.Single(vm.TranslationRows).Original);
            vm.TranslationRows[0].Translation="Привіт %s";Assert.True(vm.TranslationDirty);var old=vm.AiLocale;vm.AiLocale="de_de";Assert.Equal(old,vm.AiLocale);Assert.True(w.FindControl<TextBlock>("TranslationUnsaved")!.IsVisible);
            w.Close();Assert.True(w.IsVisible);Assert.True(vm.TranslationDirty);
            w.FindControl<Button>("SaveTranslationEdits")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.False(vm.TranslationDirty);
            w.FindControl<Button>("CreateTranslationPack")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.True(File.Exists(vm.TranslationPackPath));Assert.Contains("fr_fr-to-uk_ua",vm.TranslationPackPath);Assert.Equal(0,f.Calls);Assert.Equal(0,f.KeyReads);Assert.False(vm.AiEnabled);
            await vm.LoadTranslationDraftsAsync();Assert.Single(vm.TranslationDrafts);Assert.Equal("Привіт %s",Assert.Single(vm.TranslationRows).Translation);
        }finally{w.DataContext=null;w.Close();}
    }
    [AvaloniaFact] public async Task DirectionChangesAndExternalEditsInvalidateTranslationConsentBeforeRequest()
    {
        using var f=new AiUxFixture();using var services=new LauncherServices(f.Workspace.Paths,f.Transport(),()=>{f.KeyReads++;return "synthetic-api-key-only";});await services.Store.InitializeAsync();var p=f.Workspace.Profile();await services.Store.SaveProfileAsync(p);var vm=new MainViewModel(services);await vm.InitializeAsync();vm.AiEnabled=true;vm.AiModel="fixture-model";
        await vm.ImportTranslationJarsAsync([f.Jar()]);vm.AiSourceLocale="fr_fr";await vm.ReadTranslationResourcesAsync();await vm.PreviewTranslationTransferAsync();vm.TranslationConsent=true;vm.AiLocale="de_de";Assert.False(vm.TranslationConsent);await vm.TranslateModsAsync();Assert.Equal(0,f.Calls);
        vm.SelectedTranslationDraft=Assert.Single(vm.TranslationDrafts);await vm.PreviewTranslationTransferAsync();vm.TranslationConsent=true;await services.Translations.SaveManualAsync(p,"uk_ua","demo",new Dictionary<string,string>{{"hello","Локально %s"}},sourceLocale:"fr_fr");await vm.TranslateModsAsync();Assert.False(vm.LastOperationSucceeded);Assert.Equal(0,f.Calls);Assert.Equal(0,f.KeyReads);
    }
    [AvaloniaTheory][InlineData(980,680)][InlineData(1220,800)] public async Task EightLanguagesAndSmallLogicalWindowKeepNamedControlsAndLabels(int width,int height)
    {
        using var f=new AiUxFixture();using var services=new LauncherServices(f.Workspace.Paths);var vm=new MainViewModel(services);await vm.InitializeAsync();var w=new MainWindow{DataContext=vm,Width=width,Height=height};w.Show();try
        {
            vm.Page=LauncherPage.Settings;foreach(var language in TextCatalog.Languages)
            {
                vm.SelectedLanguage=language;TextCatalog.ValidateCatalog(language.Code,TextCatalog.Catalog(language.Code));
                w.FindControl<TabControl>("SettingsTabs")!.SelectedItem=w.FindControl<TabItem>("AiConnectionTab");w.UpdateLayout();
                Assert.Equal(TextCatalog.Get("AiInDevelopment"), w.FindControl<TextBlock>("AiConnectionDevelopmentLabel")!.Text);
                Assert.Equal(TextCatalog.Get("AiUxCheckConnection"),w.FindControl<Button>("CheckAiConnectionButton")!.Content);
                var input=w.FindControl<TextBox>("AiKeyInput")!;Assert.True(input.Bounds.Width>0);Assert.Equal(width,w.Bounds.Width);Assert.NotNull(w.FindControl<ListBox>("TranslationTable"));Assert.NotNull(w.FindControl<ComboBox>("TranslationProfile"));
                Assert.True(input.Focus());w.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);w.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);Assert.NotSame(input,w.FocusManager!.GetFocusedElement());
            }
        }finally{w.DataContext=null;w.Close();TextCatalog.Apply("uk");}
    }
}
