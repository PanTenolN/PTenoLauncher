using System.Globalization;
namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    static TextCatalog()
    {
        Texts.Add("Message000", ("Кошик порожній.", "The cart is empty."));
        Texts.Add("Message001", ("ОФЛАЙН · мережеві дії вимкнені", "OFFLINE · network actions are disabled"));
        Texts.Add("Message002", ("ОНЛАЙН · локальні дані доступні завжди", "ONLINE · local data is always available"));
        Texts.Add("Message003", ("Готово.", "Ready."));
        Texts.Add("Message004", ("Каталог ще не завантажено.", "The catalog has not been loaded yet."));
        Texts.Add("Message005", ("Вхід не виконано", "Not signed in"));
        Texts.Add("Message006", ("Оберіть профіль і перевірте файли, Java та підтверджений акаунт.", "Select a profile and check game files, Java and a verified account."));
        Texts.Add("Message007", ("Резервну копію перевірено: ", "Backup verified: "));
        Texts.Add("Message008", ("Профіль архівовано. Файли збережено.", "Profile archived. Files have been preserved."));
        Texts.Add("Message009", ("Каталог оновлено з Mojang.", "Catalog refreshed from Mojang."));
        Texts.Add("Message010", ("Налаштування збережено.", "Settings saved."));
        Texts.Add("Message011", ("Виконується офіційний вхід через Microsoft…", "Signing in through Microsoft…"));
        Texts.Add("Message012", ("Ліцензію Minecraft Java Edition підтверджено.", "Minecraft Java Edition ownership verified."));
        Texts.Add("Message013", ("Збережені облікові дані видалено.", "Saved account credentials removed."));
        Texts.Add("Message014", ("Перевірку завершено.", "Check complete."));
        Texts.Add("Message015", ("Встановлено Quilt ", "Quilt installed: "));
        Texts.Add("Message016", ("Встановлено Fabric ", "Fabric installed: "));
        Texts.Add("Message017", ("Minecraft запускається. Зачекайте завершення гри, щоб редагувати профіль.", "Minecraft is starting. Wait for the game to exit before editing the profile."));
        Texts.Add("Message018", ("Потрібен повторний офіційний вхід: токени недоступні.", "Sign in again: protected credentials are unavailable."));
        Texts.Add("Message019", ("Готово. Локальні дані завантажено без мережевих запитів.", "Ready. Local data loaded without network requests."));
        Texts.Add("Message020", ("Дію скасовано. Завершені файли збережено.", "Operation cancelled. Completed files have been preserved."));
        Texts.Add("Message021", ("Помилка Microsoft-входу. Перевірте реєстрацію застосунку та дозвіл API.", "Microsoft sign-in failed. Check application registration and API access."));
        Texts.Add("Message022", ("Спочатку оберіть і збережіть профіль.", "Select and save a profile first."));
        Texts.Add("Message023", ("RAM має бути цілим числом MB.", "Memory must be a whole number of MB."));
        Texts.Add("Message024", ("Для іншої версії встановленої гри створіть новий профіль. Це захистить світи.", "Create a new profile to change an installed game version and protect your worlds."));
        Texts.Add("Message025", ("Профіль збережено.", "Profile saved."));
        Texts.Add("Message026", ("Відновлено в новий профіль. Встановіть файли Minecraft повторно або відновіть їх зі спільного кешу.", "Restored into a new profile. Reinstall Minecraft files or restore them from the shared cache."));
        Texts.Add("Message027", (" · Локальний JAR · ", " · Local JAR · "));
        Texts.Add("Message028", (" MB; залежності: ", " MB; dependencies: "));
        Texts.Add("Message029", ("Локальні файли прочитано без мережі. Натисніть «Знайти залежності», щоб розрахувати спільний кошик.", "Local files inspected without network access. Select Resolve dependencies to calculate the combined cart."));
        Texts.Add("Message030", ("Без вибору використовується вже встановлена або найновіша стабільна версія Fabric.", "Without a selection, the installed or latest stable Fabric version is used."));
        Texts.Add("Message031", ("Виберіть профіль Fabric.", "Select a Fabric profile."));
        Texts.Add("Message032", (" Використано кеш метаданих.", " Cached metadata was used."));
        Texts.Add("Message033", ("Виберіть версію Fabric, потім натисніть «Встановити / відновити».", "Select a Fabric version, then select Install / repair."));
        Texts.Add("Message034", ("Тему збережено.", "Theme saved."));
        Texts.Add("Message035", ("AI: вимкнено. Основні функції працюють незалежно.", "AI is disabled. Core features work independently."));
        Texts.Add("Message036", ("Профіль повернуто з архіву.", "Profile unarchived."));
        Texts.Add("Message037", ("Профіль видалено; очищення тимчасових файлів буде повторено під час наступного запуску.", "Profile deleted; temporary-file cleanup will be retried on the next startup."));
        Texts.Add("Message038", ("Профіль остаточно видалено. Спільні компоненти збережено.", "Profile permanently deleted. Shared components preserved."));
        Texts.Add("Message039", ("Vanilla не завантажує моди.", "Vanilla does not load mods."));
        Texts.Add("Message040", ("Відкриття теки через Провідник підтримується у Windows.", "Opening folders in Explorer is supported on Windows."));
        Texts.Add("Message041", ("Відкрито теку модів: ", "Opened mods folder: "));
        Texts.Add("Message042", ("Контрольні хеші локальних модів збережено. Це не антивірусний висновок.", "Local mod integrity hashes saved. This is not an antivirus assessment."));
        Texts.Add("Message043", (" (кеш).", " (cached)."));
        Texts.Add("Message044", ("Виберіть мод.", "Select a mod."));
        Texts.Add("Message045", ("Вибір необов'язкових компонентів змінено. Застосуйте його й перевірте новий кошик.", "Optional selections changed. Apply them and review the recalculated cart."));
        Texts.Add("Message046", ("Профіль або кошик змінено; план відхилено.", "Profile or cart changed; the plan was rejected."));
        Texts.Add("Message047", ("Локальний JAR / Unknown Source", "Local JAR / Unknown Source"));
        Texts.Add("Message048", ("Локальний JAR / Modrinth SHA512", "Local JAR / Modrinth SHA512"));
        Texts.Add("Message049", ("Локальних файлів: ", "Local files: "));
        Texts.Add("Message050", ("; каталог: ", "; catalog: "));
        Texts.Add("Message051", ("; завантаження: ", "; download: "));
        Texts.Add("Message052", ("Використано лише локальні метадані; зовнішні залежності не підтверджені.", "Only local metadata was used; external dependencies are unverified."));
        Texts.Add("Message053", ("Використано кешовані метадані; новіші версії не підтверджені.", "Cached metadata was used; newer versions are unverified."));
        Texts.Add("Message054", ("Використано доступні метадані Modrinth.", "Available Modrinth metadata was used."));
        Texts.Add("Message055", ("Блокування: ", "Blocked: "));
        Texts.Add("Message056", ("Попередження: ", "Warning: "));
        Texts.Add("Message057", ("Спільний кошик розраховано. Перегляньте всі файли й підтвердьте встановлення.", "Combined cart calculated. Review all files and confirm installation."));
        Texts.Add("Message058", ("Кошик містить помилки; встановлення заблоковано.", "The cart contains errors; installation is blocked."));
        Texts.Add("Message059", ("Спочатку виберіть версію мода.", "Select a mod version first."));
        Texts.Add("Message060", ("Спочатку розрахуйте спільний кошик кнопкою «Знайти залежності».", "Calculate the combined cart using Resolve dependencies first."));
        Texts.Add("Message061", ("Моди: ", "Mods: "));
        Texts.Add("Message062", ("Спільний кошик встановлено однією транзакцією. Для гри потрібні встановлений Mod Loader, Java й підтверджений акаунт.", "Combined cart installed in one transaction. Playing requires an installed mod loader, Java and a verified account."));
        Texts.Add("Message063", ("Без вибору використовується встановлена або найновіша релізна версія Quilt. Метадані модів Quilt ще потребують підтримки.", "Without a selection, the installed or latest release Quilt version is used. Some Quilt mod metadata remains unsupported."));
        Texts.Add("Message064", ("Виберіть профіль Quilt.", "Select a Quilt profile."));
        Texts.Add("Message065", ("Версій Quilt: ", "Quilt versions: "));
        Texts.Add("Message066", (". Використано кеш метаданих.", ". Cached metadata was used."));
        Texts.Add("Message067", ("Виберіть версію Quilt і натисніть «Встановити / відновити».", "Select a Quilt version, then select Install / repair."));
        Texts.Add("Message068", ("Невідомий компонент", "Unknown component"));
        Texts.Add("Message069", (" · для ", " · for "));
        Texts.Add("Message070", ("Немає підтвердженого ID; автоматичний вибір недоступний.", "No verified ID; automatic selection is unavailable."));
        Texts.Add("Message071", ("Modrinth · точна версія ", "Modrinth · exact version "));
        Texts.Add("Message072", ("Modrinth · сумісну версію буде підібрано під час застосування вибору.", "Modrinth · a compatible version will be selected when applying choices."));
        Texts.Add("Snapshots", ("Снапшоти", "Snapshots"));
        Texts.Add("HistoricalVersions", ("Альфа / Бета", "Alpha / Beta"));
        Texts.Add("AutomaticJava", ("Автоматично / java.exe", "Automatic / java.exe"));
        Texts.Add("ReadinessTitle", ("Готовність до офлайн-гри", "Offline readiness"));
        Texts.Add("ArchivedBadge", ("В архіві", "Archived"));
        Texts.Add("BackupPicker", ("Резервна копія PTenoLauncher", "PTenoLauncher backup"));
        Texts.Add("BackupFile", ("Резервна копія", "Backup"));
        Texts.Add("ModsPicker", ("Локальні Minecraft JAR", "Local Minecraft JARs"));
        Texts.Add("ProfileWindowTitle", ("PTenoLauncher — Профіль", "PTenoLauncher — Profile"));
        Texts.Add("AiEnabledStatus", ("AI увімкнено; провайдера не налаштовано.", "AI enabled; provider not configured."));
        Texts.Add("ProfileCount", ("Профілів: {0}", "Profiles: {0}"));
        Texts.Add("DownloadProgress", ("Завантаження {0}: {1:N0} / {2:N0} KB", "Downloading {0}: {1:N0} / {2:N0} KB"));
        Texts.Add("GameInstalled", ("Minecraft {0}: перевірено {1} файлів. Потрібна Java {2}.", "Minecraft {0}: verified {1} files. Java {2} is required."));
        Texts.Add("GameExit", ("Minecraft завершено. Код завершення: {0}.", "Minecraft exited. Exit code: {0}."));
        Texts.Add("CatalogSummary", ("Кеш: {0:yyyy-MM-dd HH:mm} UTC · останній реліз {1} · снапшот {2}", "Cache: {0:yyyy-MM-dd HH:mm} UTC · latest release {1} · snapshot {2}"));
        Texts.Add("FabricCount", ("Версій Fabric: {0}.", "Fabric versions: {0}."));
        Texts.Add("SearchCount", ("Modrinth: результатів — {0}", "Modrinth: {0} results"));
        Texts.Add("CompatibleCount", ("Сумісних версій: {0}.", "Compatible versions: {0}."));
        Texts.Add("ModCount", ("Модів: {0}", "Mods: {0}"));
        Texts.Add("FilesSize", ("Файлів: {0} · {1:N2} MB", "Files: {0} · {1:N2} MB"));
        Texts.Add("AuthServiceError", ("Помилка входу: {0}, HTTP {1}. Етап: {2}. Перевірте доступ застосунку та стан акаунта. Сам код HTTP не визначає причину.", "Sign-in failed: {0}, HTTP {1}. Stage: {2}. Check application access and account status. The HTTP code alone does not establish the cause."));
        Texts.Add("ReadinessReady", ("Готово до офлайн-гри: {0} файлів Minecraft і {1} модів перевірено.", "Ready for offline play: {0} Minecraft files and {1} mods verified."));
        Texts.Add("ReadinessDiagnostic", ("Діагностика [{0}]: {1}", "Diagnostic [{0}]: {1}"));
        Texts.Add("Readiness.loader.unsupported", ("Встановлення та запуск цього Mod Loader ще не реалізовано.", "Installing and launching this mod loader is not implemented yet."));
        Texts.Add("Readiness.manifest.corrupt", ("Пошкоджено локальний опис встановлення.", "The local installation record is corrupt."));
        Texts.Add("Readiness.game.missing", ("Minecraft ще не встановлено для цього профілю.", "Minecraft is not installed for this profile yet."));
        Texts.Add("Readiness.loader.mismatch", ("Встановіть вибраний Mod Loader або Vanilla для цього профілю.", "Install the selected mod loader or Vanilla for this profile."));
        Texts.Add("Readiness.version.mismatch", ("Встановлена версія відрізняється від налаштувань профілю.", "The installed version differs from the profile settings."));
        Texts.Add("Readiness.file.invalid", ("Необхідний файл гри відсутній або пошкоджений.", "A required game file is missing or corrupt."));
        Texts.Add("Readiness.java.missing", ("Оберіть Java x64 для профілю.", "Select 64-bit Java for this profile."));
        Texts.Add("Readiness.java.corrupt", ("Пошкоджено керований Java Runtime. Відновіть Java.", "The managed Java runtime is corrupt. Repair Java."));
        Texts.Add("Readiness.java.arch", ("Потрібна Java x64.", "64-bit Java is required."));
        Texts.Add("Readiness.java.version", ("Обрана Java не відповідає версії, потрібній для гри.", "The selected Java version does not match the game's required version."));
        Texts.Add("Readiness.java.invalid", ("Java не пройшла перевірку.", "Java verification failed."));
        Texts.Add("Readiness.account.missing", ("Спочатку увійдіть онлайн і підтвердьте ліцензію Minecraft Java Edition.", "Sign in online first and verify Minecraft Java Edition ownership."));
        RegisterAiTexts();
        LoadAdditionalLanguages();
    }
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.GetCultureInfo(Language == "en" ? "en-IE" : Language == "uk" ? "uk-UA" : Language), Get(key), args);
    public static string Relocalize(string value)
    {
        foreach (var pair in Texts)
        {
            if (value == pair.Value.Uk || value == pair.Value.En || Additional.Values.Any(c => c.TryGetValue(pair.Key, out var translated) && translated == value))
                return Get(pair.Key);
        }
        return value;
    }
}
