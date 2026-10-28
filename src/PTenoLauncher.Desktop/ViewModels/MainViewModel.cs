using System.Collections.ObjectModel;
using System.Globalization;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly LauncherServices services;
    private readonly List<AsyncCommand> commands = [];
    private CancellationTokenSource? operation;
    public ObservableCollection<GameProfile> Profiles { get; } = [];
    public ObservableCollection<VersionEntry> Versions { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ObservableCollection<ModCartItem> Cart { get; } = [];
    public ModLoader[] Loaders { get; } = Enum.GetValues<ModLoader>();
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand ArchiveCommand { get; }
    public AsyncCommand RefreshVersionsCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand SignInCommand { get; }
    public AsyncCommand SignOutCommand { get; }
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand InstallCommand { get; }
    public AsyncCommand LaunchCommand { get; }
    public AsyncCommand InstallCartCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand CancelCommand { get; }
    private GameProfile? selectedProfile;
    public GameProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!Set(ref selectedProfile, value)) return;
            if (value is not null)
            {
                ProfileName = value.Name; VersionId = value.MinecraftVersion; Loader = value.Loader;
                Memory = value.MemoryMb.ToString(CultureInfo.InvariantCulture); JavaPath = value.JavaPath ?? "";
            }
            ResetFabricSelection(); ResetQuiltSelection();
            ClearRemotePlan(); RemoteSearchResults.Clear(); RemoteVersions.Clear(); SelectedRemoteProject = null; SelectedRemoteVersion = null;
            Cart.Clear(); CartSummary = "Кошик порожній."; UnknownSourceAccepted = false;
            Changed(nameof(ProfileCount)); SelectionChanged();
        }
    }
    private VersionEntry? selectedVersion;
    public VersionEntry? SelectedVersion { get => selectedVersion; set { if (Set(ref selectedVersion, value) && value is not null && !updatingVersions) VersionId = value.Id; } }
    private string profileName = "";
    public string ProfileName { get => profileName; set => Set(ref profileName, value); }
    private string versionId = "";
    public string VersionId { get => versionId; set => Set(ref versionId, value); }
    private ModLoader loader;
    public ModLoader Loader { get => loader; set => Set(ref loader, value); }
    private string memory = "4096";
    public string Memory { get => memory; set => Set(ref memory, value); }
    private string javaPath = "";
    public string JavaPath { get => javaPath; set => Set(ref javaPath, value); }
    private string clientId = "";
    public string ClientId { get => clientId; set => Set(ref clientId, value); }
    private bool offline;
    public bool Offline { get => offline; set { Set(ref offline, value); services.Network.Offline = value; Changed(nameof(ModeText)); } }
    private bool busy;
    public bool IsBusy { get => busy; private set { Set(ref busy, value); Changed(nameof(IsIdle)); foreach (var command in commands) command.Refresh(); CancelCommand.Refresh(); } }
    public bool IsIdle => !IsBusy;
    public string ModeText => Offline ? "ОФЛАЙН · мережеві дії вимкнені" : "ОНЛАЙН · локальні дані доступні завжди";
    public string ProfileCount => $"{Profiles.Count} профілів";
    private string status = "Готово.";
    public string Status { get => status; set => Set(ref status, value); }
    private string catalogStatus = "Каталог ще не завантажено.";
    public string CatalogStatus { get => catalogStatus; set => Set(ref catalogStatus, value); }
    private string account = "Вхід не виконано";
    public string Account { get => account; set => Set(ref account, value); }
    private string readiness = "Оберіть профіль і перевірте файли, Java та підтверджений акаунт.";
    public string Readiness { get => readiness; set => Set(ref readiness, value); }
    private string cartSummary = "Кошик порожній.";
    public string CartSummary { get => cartSummary; set => Set(ref cartSummary, value); }
    private bool unknownSourceAccepted;
    public bool UnknownSourceAccepted { get => unknownSourceAccepted; set => Set(ref unknownSourceAccepted, value); }
    public string DataRoot => services.Paths.Root;
    public MainViewModel(LauncherServices services)
    {
        this.services = services;
        AsyncCommand Command(Func<CancellationToken, Task> action)
        {
            var command = new AsyncCommand(() => RunAsync(action), () => !IsBusy);
            commands.Add(command); return command;
        }
        CancelCommand = new(() => { operation?.Cancel(); return Task.CompletedTask; }, () => IsBusy);
        SaveCommand = Command(SaveProfileAsync);
        BackupCommand = Command(async ct => { var file = await services.Backups.CreateAsync(RequireProfile(), ct); Status = "Резервну копію перевірено: " + file; });
        NewCommand = Command(_ => { SelectedProfile = null; ProfileName = ""; VersionId = SelectedVersion?.Id ?? ""; Loader = ModLoader.Vanilla; Memory = "4096"; JavaPath = ""; return Task.CompletedTask; });
        ArchiveCommand = Command(async ct =>
        {
            var p = RequireProfile(); await services.Store.SaveProfileAsync(p with { Archived = true }, ct);
            await ReloadProfilesAsync(ct); SelectedProfile = null; Status = "Профіль архівовано. Файли збережено.";
        });
        RefreshVersionsCommand = Command(async ct => { SetCatalog(await services.Versions.RefreshAsync(ct)); Status = "Каталог оновлено з Mojang."; });
        SaveSettingsCommand = Command(async ct =>
        {
            if (!string.IsNullOrWhiteSpace(ClientId)) services.Authentication.Configure(ClientId);
            await services.Store.SetSettingAsync("microsoft.clientId", ClientId.Trim(), ct);
            await services.Store.SetSettingAsync("network.offline", Offline.ToString(), ct); Status = "Налаштування збережено.";
        });
        SignInCommand = Command(async ct =>
        {
            services.Authentication.Configure(ClientId.Trim());
            Status = "Виконується офіційний вхід через Microsoft…";
            var session = await services.Authentication.SignInAsync(true, ct);
            RefreshAccounts(); Status = "Ліцензію Minecraft Java Edition підтверджено.";
            try { await services.Skins.RefreshAsync(session.Account, ct); } catch (Exception ex) when (ex is not OperationCanceledException) { services.Log.Error("skin.download", ex); }
            RefreshAccounts(); Status = "Ліцензію Minecraft Java Edition підтверджено.";
        });
        SignOutCommand = Command(async ct => { await services.Authentication.SignOutAsync(ct); RefreshAccounts(); Status = "Збережені облікові дані видалено."; });
        CheckCommand = Command(async ct => { Readiness = (await services.Readiness.CheckAsync(RequireProfile(), ct)).Summary; Status = "Перевірку завершено."; });
        InstallCommand = Command(async ct =>
        {
            var progress = new Progress<DownloadProgress>(p => Status = $"Завантаження {p.Name}: {p.Received / 1024:N0} / {p.Total / 1024:N0} KB");
            var profile = RequireProfile();
            var result = profile.Loader switch {
                ModLoader.Fabric => await services.Fabric.InstallAsync(profile, SelectedFabricVersion?.Version, progress, ct),
                ModLoader.Quilt => await services.Quilt.InstallAsync(profile, SelectedQuiltVersion?.Version, progress, ct),
                _ => await services.Installer.InstallAsync(profile, progress, ct)
            };
            if (result.Loader == ModLoader.Quilt) QuiltStatus = "Встановлено Quilt " + result.LoaderVersion;
            if (result.Loader == ModLoader.Fabric) FabricStatus = "Встановлено Fabric " + result.LoaderVersion;
            Status = $"Minecraft {result.Version}: перевірено {result.Files.Count} файлів. Потрібна Java {result.JavaMajor}.";
        });
        LaunchCommand = Command(async ct => { Status = "Minecraft запускається. Зачекайте завершення гри, щоб редагувати профіль."; ConsoleLines.Clear(); var console = new Progress<string>(line => { ConsoleLines.Add(line); while (ConsoleLines.Count > 1000) ConsoleLines.RemoveAt(0); }); var exit = await services.Launcher.LaunchAsync(RequireProfile(), ct, console); Status = $"Minecraft завершено. Exit code: {exit}."; });
        Cart.CollectionChanged += (_, _) => { UnknownSourceAccepted = false; InvalidateModPlan(); };
        InstallCartCommand = Command(InstallCombinedAsync);
    }
    public Task InitializeAsync() => RunAsync(async ct =>
    {
        await services.Store.InitializeAsync(ct);
        awaitLanguage = await services.Store.GetSettingAsync("ui.language", ct) ?? "uk";
        await InitializeFoundationAsync(ct);
        ShowSnapshots = !bool.TryParse(await services.Store.GetSettingAsync("versions.snapshots", ct), out var snapshots) || snapshots;
        ShowHistorical = bool.TryParse(await services.Store.GetSettingAsync("versions.historical", ct), out var historical) && historical;
        Offline = bool.TryParse(await services.Store.GetSettingAsync("network.offline", ct), out var off) && off;
        var savedClientId = await services.Store.GetSettingAsync("microsoft.clientId", ct);
        ClientId = string.IsNullOrWhiteSpace(savedClientId) ? PTenoLauncher.Infrastructure.Authentication.LauncherMicrosoftApplication.ClientId : savedClientId;
        if (Guid.TryParse(ClientId, out var id) && id != Guid.Empty) services.Authentication.Configure(ClientId);
        try { RefreshAccounts(); } catch (System.Security.Cryptography.CryptographicException) { Account = "Потрібен повторний офіційний вхід: токени недоступні."; }
        await ReloadProfilesAsync(ct);
        foreach (var p in allProfiles) { try { await services.Mods.RecoverAsync(p, ct); } catch (Exception ex) when (ex is not OperationCanceledException) { services.Log.Error("mod.recovery", ex); } }
        var catalog = await services.Versions.GetCachedAsync(ct); if (catalog is not null) SetCatalog(catalog);
        Status = "Готово. Локальні дані завантажено без мережевих запитів."; IsInitialized = true;
    });
    public async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        operation = new(); IsBusy = true; LastOperationSucceeded = false;
        try { await action(operation.Token); await ReloadHistoryAsync(); LastOperationSucceeded = true; SelectionChanged(); }
        catch (OperationCanceledException) { Status = "Дію скасовано. Завершені файли збережено."; }
        catch (Exception ex)
        {
            services.Log.Error("user.operation", ex);
            Status = ex is Microsoft.Identity.Client.MsalException ? "Помилка Microsoft-входу. Перевірте реєстрацію застосунку та дозвіл API." : ex.Message;
        }
        finally { IsBusy = false; operation.Dispose(); operation = null; }
    }
    private GameProfile RequireProfile() => SelectedProfile ?? throw new InvalidOperationException("Спочатку оберіть і збережіть профіль.");
    private async Task SaveProfileAsync(CancellationToken ct)
    {
        if (!int.TryParse(Memory, out var ram)) throw new ArgumentException("RAM має бути цілим числом MB.");
        if (SelectedProfile is not null && SelectedProfile.MinecraftVersion != VersionId &&
            File.Exists(services.Paths.Receipt(SelectedProfile.Id)))
            throw new InvalidOperationException("Для іншої версії встановленої гри створіть новий профіль. Це захистить світи.");
        var p = new GameProfile(SelectedProfile?.Id ?? Guid.NewGuid(), ProfileName.Trim(), VersionId.Trim(), Loader,
            ram, string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath.Trim(), SelectedProfile?.CreatedAt ?? DateTimeOffset.UtcNow);
        await services.Store.SaveProfileAsync(p, ct); await ReloadProfilesAsync(ct);
        SelectedProfile = Profiles.Single(x => x.Id == p.Id); Status = "Профіль збережено.";
    }
    private async Task ReloadProfilesAsync(CancellationToken ct)
    {
        allProfiles = await services.Store.GetProfilesAsync(ct); ApplyProfileFilter();
    }
    private void SetCatalog(VersionCatalog catalog)
    {
        currentCatalog = catalog; ApplyVersionFilter();
        CatalogStatus = $"Кеш: {catalog.FetchedAt:yyyy-MM-dd HH:mm} UTC · останній реліз {catalog.LatestRelease} · snapshot {catalog.LatestSnapshot ?? "—"}";
    }
    private async Task ReloadHistoryAsync()
    {
        History.Clear(); foreach (var item in await services.Store.GetHistoryAsync(SelectedProfile?.Id)) History.Add(item);
    }
    public Task RestoreBackupAsync(string file) => RunAsync(async ct => { var p = await services.Backups.RestoreAsNewAsync(file, ct); await ReloadProfilesAsync(ct); SelectedProfile = Profiles.Single(x => x.Id == p.Id); Status = "Відновлено в новий профіль. Встановіть файли Minecraft повторно або відновіть їх зі спільного кешу."; });
    public Task AddModsAsync(IEnumerable<string> files) => RunAsync(async ct =>
    {
        var p = RequireProfile();
        foreach (var file in files) Cart.Add(new(file, await services.Mods.InspectAsync(file, ct)));
        CartSummary = string.Join(Environment.NewLine, Cart.Select(c =>
            (c.Inspection.Name ?? c.Inspection.FileName) + " " + c.Inspection.Version + " · Локальний JAR · " +
            (c.Inspection.Size / 1048576.0).ToString("0.##") + " MB; залежності: " + string.Join(", ", c.Inspection.RequiredDependencies)));
        Status = "Локальні файли прочитано без мережі. Натисніть «Знайти залежності», щоб розрахувати спільний кошик.";
    });
}
