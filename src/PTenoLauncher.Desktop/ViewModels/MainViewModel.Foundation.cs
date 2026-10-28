using Avalonia;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.ViewModels;
public enum LauncherPage { Home, Profiles, Mods, Modpacks, Downloads, Servers, Settings }
public sealed partial class MainViewModel
{
    internal LauncherServices Services => services;
    private LauncherPage page;
    public LauncherPage Page
    {
        get => page;
        set
        {
            if (value == LauncherPage.Mods && !CanManageMods) value = LauncherPage.Home;
            if (!Set(ref page, value)) return;
            foreach (var name in new[] { nameof(IsHome), nameof(IsProfiles), nameof(IsMods), nameof(IsModpacks), nameof(IsDownloads), nameof(IsServers), nameof(IsSettings), nameof(PageTitle) }) Changed(name);
        }
    }
    public bool IsHome => Page == LauncherPage.Home;
    public bool IsProfiles => Page == LauncherPage.Profiles;
    public bool CanManageMods => SelectedProfile is { Loader: not ModLoader.Vanilla };
    public bool IsMods => CanManageMods && Page == LauncherPage.Mods;
    public bool IsModpacks => Page == LauncherPage.Modpacks;
    public bool IsDownloads => Page == LauncherPage.Downloads;
    public bool IsServers => Page == LauncherPage.Servers;
    public bool IsSettings => Page == LauncherPage.Settings;
    public string PageTitle => TextCatalog.Get(Page.ToString());
    public bool HasProfile => SelectedProfile is not null;
    public bool NoProfile => !HasProfile;
    public string ActiveName => SelectedProfile?.Name ?? "";
    public string ActiveVersion => SelectedProfile?.Summary ?? "";
    public string ActiveIcon => SelectedProfile is { Name.Length: > 0 } p ? System.Globalization.StringInfo.GetNextTextElement(p.Name).ToUpperInvariant() : "P";
    public string ActiveMods
    {
        get
        {
            if (SelectedProfile is null) return "";
            try { return TextCatalog.Format("ModCount", Directory.EnumerateFiles(Path.Combine(services.Paths.Game(SelectedProfile.Id), "mods"), "*.jar").Count()); }
            catch (IOException) { return "mods: ?"; }
        }
    }
    private bool showArchived;
    public bool ShowArchived { get => showArchived; set { if (Set(ref showArchived, value)) ApplyProfileFilter(); } }
    private IReadOnlyList<GameProfile> allProfiles = [];
    private void ApplyProfileFilter()
    {
        var id = SelectedProfile?.Id;
        Profiles.Clear(); foreach (var p in allProfiles.Where(p => ShowArchived || !p.Archived)) Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.FirstOrDefault(p => !p.Archived);
        Changed(nameof(ProfileCount));
    }
    private void SelectionChanged()
    {
        Changed(nameof(CanManageMods)); Changed(nameof(IsMods)); Changed(nameof(IsFabricProfile)); Changed(nameof(IsQuiltProfile));
        if (!CanManageMods && Page == LauncherPage.Mods) Page = LauncherPage.Home;
        foreach (var name in new[] { nameof(HasProfile), nameof(NoProfile), nameof(ActiveName), nameof(ActiveVersion), nameof(ActiveIcon), nameof(ActiveMods) }) Changed(name);
    }
    private LauncherTheme selectedTheme;
    public LauncherTheme SelectedTheme { get => selectedTheme; set { if (Set(ref selectedTheme, value)) { Changed(nameof(IsCustomTheme)); try { ApplyTheme(); } catch (ArgumentException ex) { Status = ex.Message; } } } }
    public LauncherTheme[] Themes { get; } = Enum.GetValues<LauncherTheme>();
    public bool IsCustomTheme => SelectedTheme == LauncherTheme.Custom;
    private string customBackground = "#111315", customSidebar = "#191B1E", customSurface = "#222529", customAccent = "#A3B3C7", customText = "#E5E7EB";
    public string CustomBackground { get => customBackground; set => Set(ref customBackground, value); }
    public string CustomSidebar { get => customSidebar; set => Set(ref customSidebar, value); }
    public string CustomSurface { get => customSurface; set => Set(ref customSurface, value); }
    public string CustomAccent { get => customAccent; set => Set(ref customAccent, value); }
    public string CustomText { get => customText; set => Set(ref customText, value); }
    private double customRadius = 6;
    public double CustomRadius { get => customRadius; set => Set(ref customRadius, value); }
    private bool animations = true;
    public bool Animations { get => animations; set => Set(ref animations, value); }
    private UiLanguage selectedLanguage = TextCatalog.Languages[0];
    public UiLanguage SelectedLanguage { get => selectedLanguage; set { if (Set(ref selectedLanguage, value) && value is not null) { TextCatalog.Apply(value.Code); foreach (var name in new[] { nameof(PageTitle), nameof(ModeText), nameof(ProfileCount), nameof(Status), nameof(CatalogStatus), nameof(Readiness), nameof(CartSummary), nameof(ActiveMods), nameof(FabricStatus), nameof(QuiltStatus), nameof(RemoteSummary) }) Changed(name); foreach (var choice in OptionalChoices) choice.RefreshLanguage(); if (NeedsSignIn) Account = TextCatalog.Get("Message005"); } } }
    public UiLanguage[] Languages => TextCatalog.Languages;
    private bool aiEnabled;
    public bool AiEnabled { get => aiEnabled; set { Set(ref aiEnabled, value); services.Ai.SetEnabled(value); } }
    public bool LastOperationSucceeded { get; private set; }
    public bool IsInitialized { get; private set; }
    public System.Collections.ObjectModel.ObservableCollection<string> ConsoleLines { get; } = [];

    private CustomPalette lastValidCustom = new();
    private ThemePreferences CurrentTheme() => new(SelectedTheme, SelectedTheme == LauncherTheme.Custom ? new(CustomBackground, CustomSidebar, CustomSurface, CustomAccent, CustomText, CustomRadius, Animations) : lastValidCustom);
    private void ApplyTheme()
    {
        var theme = CurrentTheme(); PreferencesValidation.Validate(theme); lastValidCustom = theme.Custom ?? new();
        if (Application.Current is App app) app.Appearance.Apply(theme);
    }
    public Task SaveThemeAsync() => RunAsync(async ct => { ApplyTheme(); await services.Preferences.SetThemeAsync(CurrentTheme(), ct); Status = TextCatalog.Language == "en" ? "Theme saved." : TextCatalog.Get("Message034"); });
    public Task SaveLanguageAsync() => RunAsync(async ct => { await services.Store.SetSettingAsync("ui.language", SelectedLanguage.Code, ct); });
    public async Task SaveAiAsync()
    {
        try { await services.Preferences.SetAiEnabledAsync(AiEnabled); Status = AiEnabled ? TextCatalog.Get("AiEnabledStatus") : TextCatalog.Get("Message035"); }
        catch (Exception ex) { Status = ex.Message; services.Log.Error("ai.preference", ex); }
    }
    private async Task InitializeFoundationAsync(CancellationToken ct)
    {
        await services.Deletion.RecoverAsync(ct);
        var theme = await services.Preferences.GetThemeAsync(ct); var palette = theme.Custom ?? new(); lastValidCustom = palette;
        CustomBackground = palette.Background; CustomSidebar = palette.Sidebar; CustomSurface = palette.Surface;
        CustomAccent = palette.Accent; CustomText = palette.Text; CustomRadius = palette.Radius; Animations = palette.Animations;
        SelectedTheme = theme.Theme; ApplyTheme();
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == awaitLanguage) ?? Languages[0];
        TextCatalog.Apply(SelectedLanguage.Code);
        AiEnabled = await services.Preferences.GetAiEnabledAsync(ct);
    }
    private string awaitLanguage = "uk";
    public Task UnarchiveAsync() => RunAsync(async ct =>
    {
        var p = RequireProfile(); await services.Store.SaveProfileAsync(p with { Archived = false }, ct);
        await ReloadProfilesAsync(ct); Status = TextCatalog.Get("Message036");
    });
    public Task<ProfileDeletionPlan> GetDeletionPlanAsync(bool backups) => services.Deletion.PreviewAsync(RequireProfile().Id, backups);
    public Task DeleteApprovedAsync(ProfileDeletionPlan plan, string typedName) => RunAsync(async ct =>
    {
        var result = await services.Deletion.DeleteAsync(plan, typedName, ct);
        await ReloadProfilesAsync(ct); Page = LauncherPage.Home;
        Status = result.CleanupPending ? TextCatalog.Get("Message037") : TextCatalog.Get("Message038");
    });
    public Task PrepareJavaAsync() => RunAsync(async ct =>
    {
        var p = RequireProfile();
        var progress = new Progress<DownloadProgress>(d => Status = $"Java: {d.Received / 1048576.0:N1} / {d.Total / 1048576.0:N1} MB");
        var runtime = await services.Runtimes.EnsureAsync(p, progress, ct);
        await services.Store.SaveProfileAsync(p with { JavaPath = runtime.Path }, ct);
        await ReloadProfilesAsync(ct); Status = $"Java {runtime.Major} x64: {runtime.Path}";
    });

    public System.Collections.ObjectModel.ObservableCollection<PTenoLauncher.Infrastructure.Authentication.AuthAccountSummary> Accounts { get; } = [];
    private PTenoLauncher.Infrastructure.Authentication.AuthAccountSummary? selectedAccount;
    public PTenoLauncher.Infrastructure.Authentication.AuthAccountSummary? SelectedAccount { get => selectedAccount; set => Set(ref selectedAccount, value); }
    private string accountUuid = "";
    public string AccountUuid
    {
        get => accountUuid;
        private set { if (Set(ref accountUuid, value)) { Changed(nameof(HasAccount)); Changed(nameof(NeedsSignIn)); } }
    }
    public bool HasAccount => !string.IsNullOrWhiteSpace(AccountUuid);
    public bool NeedsSignIn => !HasAccount;
    private Avalonia.Media.Imaging.Bitmap? accountSkin;
    public Avalonia.Media.Imaging.Bitmap? AccountSkin { get => accountSkin; private set { var old = accountSkin; Set(ref accountSkin, value); old?.Dispose(); } }
    private void RefreshAccounts()
    {
        var session = services.Authentication.GetCachedSession();
        Accounts.Clear(); foreach (var account in services.Authentication.GetAccounts()) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Uuid == session?.Account.Uuid);
        Account = session?.Account.Name ?? TextCatalog.Get("Message005"); AccountUuid = session?.Account.Uuid ?? "";
        var skin = session is null ? null : services.Skins.Cached(session.Account);
        try { AccountSkin = skin is null ? null : new Avalonia.Media.Imaging.Bitmap(skin); }
        catch (Exception ex) when (ex is IOException or ArgumentException) { AccountSkin = null; }
        Changed(nameof(AccountUuid));
    }
    public Task SelectAccountAsync() => RunAsync(async ct =>
    {
        if (SelectedAccount is null) return;
        await services.Authentication.SelectAccountAsync(SelectedAccount.Uuid, ct); RefreshAccounts();
    });

}
