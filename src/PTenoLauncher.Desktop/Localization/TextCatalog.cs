using Avalonia;
namespace PTenoLauncher.Desktop.Localization;

public sealed record UiLanguage(string Code, string Name) { public override string ToString() => Name; }
public static partial class TextCatalog
{
    public static readonly UiLanguage[] Languages = [new("uk", "Українська"), new("en", "English"),
        new("pl", "Polski"), new("de", "Deutsch"), new("es", "Español"),
        new("fr", "Français"), new("pt-BR", "Português (Brasil)"), new("ru", "Русский")];
    public static string Language { get; private set; } = "uk";
    private static readonly Dictionary<string,(string Uk,string En)> Texts = new()
    {
        ["FirstRunTitle"]=("Оберіть мову / Choose your language", "Choose your language"),
        ["FirstRunHint"]=("Пізніше мову можна змінити в налаштуваннях.", "You can change the language later in Settings."),
        ["FirstRunContinue"]=("Продовжити", "Continue"),
        ["FirstRunExit"]=("Вийти", "Exit"),
        ["FirstRunSaveError"]=("Не вдалося зберегти мову. Спробуйте ще раз або перевірте доступ до теки даних.", "Could not save the language. Try again or check access to the data folder."),
        ["StopGame"]=("Зупинити Minecraft", "Stop Minecraft"),
        ["StopWarning"]=("За можливості закрий Minecraft через меню гри. Примусова зупинка завершує процес гри та його дочірні процеси й може призвести до втрати незбережених змін у світі.", "Close Minecraft through its game menu whenever possible. Forcing a stop ends the game and its child processes and may lose unsaved world changes."),
        ["StopAccept"]=("Я розумію ризик втрати незбережених змін", "I understand that unsaved changes may be lost"),
        ["Files"]=("Файлів", "Files"), ["Bytes"]=("байт", "bytes"),
        ["ModBaselineTitle"]=("Перевірити локальні моди…", "Review local mods…"),
        ["ModBaselineHint"]=("Перегляньте всі поточні JAR, їхній розмір і SHA-256. Підтвердження зберігає контрольні хеші для виявлення майбутніх змін. Воно не підтверджує безпечність файлів і не замінює антивірус. Файли модів і світи не змінюються.", "Review the current JAR files, sizes and SHA-256 hashes. Confirmation records a baseline for detecting future changes. It does not establish file safety or replace antivirus scanning. Mod files and worlds are not changed."),
        ["ModBaselineAccept"]=("Переглянув перелік і підтверджую ці локальні файли", "I reviewed the list and confirm these local files"),
        ["ModBaselineConfirm"]=("Зберегти контрольні хеші", "Save integrity baseline"),
        ["ModBaselineRemoved"]=("Відсутній файл буде вилучено зі списку контролю", "Missing file will be removed from the baseline"),
        ["OfflineModsHint"]=("Офлайн можна додавати локальні JAR і використовувати наявний кеш. Для нових завантажень та відсутніх залежностей потрібен інтернет.", "Offline, you can add local JARs and use existing cached files. New downloads and missing dependencies require internet access."),
        ["OpenModsFolder"]=("Перейти до теки", "Open mods folder"),
        ["PerformSignIn"]=("Виконати вхід", "Sign in"),
        ["OptionalComponents"]=("Необов’язкові компоненти", "Optional components"),
        ["OptionalHint"]=("Виберіть потрібні компоненти й застосуйте вибір. Залежності та сумісність буде розраховано повторно. Уже встановлені моди не видаляються.", "Select components and apply your choices to recalculate dependencies and compatibility. Installed mods are not removed."),
        ["ApplyOptional"]=("Застосувати вибір", "Apply selection"),
        ["ClearOptional"]=("Скинути необов’язковий вибір", "Clear optional selection"),
        ["ResolveCart"]=("Знайти залежності", "Resolve dependencies"),
        ["Search"]=("Пошук","Search"),
        ["LoadVersions"]=("Завантажити версії","Load versions"),
        ["RemoteCart"]=("Кошик Modrinth","Modrinth cart"),
        ["InstallRemote"]=("Встановити кошик Modrinth","Install Modrinth cart"),
        ["AcceptRemote"]=("Перевірив перелік і погоджуюсь установити моди, які виконують Java-код","I reviewed the list and accept installing mods that execute Java code"),
        ["RemoteHint"]=("Пошук і залежності працюють без ШІ. Під час явного пошуку залежностей до Modrinth передаються SHA-512 установлених і вибраних локальних JAR; самі файли не надсилаються. Fabric встановлюється кнопкою «Встановити / відновити». Quilt Loader також встановлюється; метадані модів Quilt, Forge і NeoForge ще потребують підтримки.","Search and dependencies work without AI. When you explicitly resolve dependencies, installed and selected local JAR SHA-512 hashes are sent to Modrinth for identification; files are not uploaded. Install Fabric using Install / repair. Quilt Loader installation is also available; Quilt, Forge and NeoForge mod metadata remain incomplete."),
        ["QuiltVersions"]=("Версії Quilt", "Quilt versions"),
        ["FabricVersions"]=("Версії Fabric", "Fabric versions"),
        ["Home"]=("Головна","Home"), ["Profiles"]=("Профілі","Profiles"), ["Mods"]=("Моди","Mods"),
        ["Modpacks"]=("Модпаки","Modpacks"), ["Downloads"]=("Завантаження","Downloads"), ["Servers"]=("Сервери","Servers"),
        ["Settings"]=("Налаштування","Settings"), ["CreateProfile"]=("Створити профіль","Create profile"),
        ["EditProfile"]=("Налаштування профілю","Profile settings"), ["Play"]=("Грати","Play"),
        ["Offline"]=("Офлайн-режим","Offline mode"), ["NoProfiles"]=("Створіть свій перший профіль","Create your first profile"),
        ["NoProfilesHint"]=("Окрема версія гри, моди та світи для кожного профілю.","Separate game versions, mods and worlds for every profile."),
        ["ReadyCheck"]=("Підготувати до офлайн-гри","Prepare for offline play"), ["InstallGame"]=("Встановити / відновити","Install / repair"),
        ["JavaAuto"]=("Підібрати й установити Java","Find / install Java"), ["Archive"]=("Архівувати","Archive"),
        ["Unarchive"]=("Повернути з архіву","Unarchive"), ["Delete"]=("Видалити профіль…","Delete profile…"),
        ["Backup"]=("Створити резервну копію","Create backup"), ["Restore"]=("Відновити копію…","Restore backup…"),
        ["Name"]=("Назва","Name"), ["Version"]=("Версія Minecraft","Minecraft version"), ["Loader"]=("Завантажувач модів","Mod loader"),
        ["Memory"]=("Пам'ять (MB)","Memory (MB)"), ["JavaPath"]=("Java для профілю","Profile Java"),
        ["Browse"]=("Обрати…","Browse…"), ["Save"]=("Зберегти","Save"), ["Cancel"]=("Скасувати","Cancel"),
        ["RefreshCatalog"]=("Оновити каталог","Refresh catalog"), ["Theme"]=("Оформлення","Appearance"),
        ["Language"]=("Мова","Language"), ["ThemeHint"]=("Зміни застосовуються без перезапуску.","Changes apply without restarting."),
        ["Background"]=("Фон","Background"), ["Sidebar"]=("Бічна панель","Sidebar"), ["Surface"]=("Поверхня","Surface"),
        ["Accent"]=("Акцент","Accent"), ["TextColor"]=("Колір тексту","Text color"), ["Radius"]=("Заокруглення","Corner radius"),
        ["Animations"]=("Анімації","Animations"), ["ApplyTheme"]=("Застосувати тему","Apply theme"),
        ["AiTitle"]=("Штучний інтелект","Artificial intelligence"), ["AiEnable"]=("Увімкнути AI-функції","Enable AI features"),
        ["AiHint"]=("ШІ необов'язковий. Моди, залежності, гра та локальні інструменти працюють незалежно. AI-провайдер ще не підключено.","AI is optional. Mods, dependencies, game and local tools are independent. No AI provider is connected yet."),
        ["Account"]=("Акаунт Microsoft","Microsoft account"), ["SignIn"]=("Увійти через Microsoft","Sign in with Microsoft"),
        ["SignOut"]=("Вийти з акаунта","Sign out"), ["ClientId"]=("Власний Application (client) ID","Your Application (client) ID"),
        ["AccountHint"]=("Увійдіть через Microsoft у системному браузері. Після перевірки права на Minecraft Java Edition тут з’явиться ваш акаунт.","Sign in with Microsoft in your browser. Your account appears here after Minecraft Java Edition ownership is verified."),
        ["History"]=("Історія","History"), ["Console"]=("Консоль гри","Game console"),
        ["LocalMods"]=("Додати локальні JAR…","Add local JARs…"), ["DropHint"]=("Перетягніть JAR сюди або оберіть декілька файлів.","Drop JAR files here or select multiple files."),
        ["InstallCart"]=("Установити кошик","Install cart"), ["AcceptUnknown"]=("Розумію попередження про непідтверджені файли","I accept warnings for unverified files"),
        ["Resolve"]=("Знайти мод і залежності","Find mod and dependencies"), ["ModrinthQuery"]=("Modrinth: назва або slug проєкту","Modrinth: project name or slug"),
        ["Cart"]=("Кошик установлення","Installation cart"), ["ClearCart"]=("Очистити кошик","Clear cart"),
        ["Pending"]=("Цей розділ ще не реалізовано. Його вимоги збережено в загальному плані розробки.","This section is not implemented yet. Its requirements remain in the development roadmap."),
        ["DeleteTitle"]=("Остаточне видалення профілю","Permanently delete profile"), ["DeleteWarning"]=("Буде втрачено світи й усі робочі файли цього профілю. Спільний кеш і Java зберігаються.","This deletes this profile's worlds and working files. Shared cache and Java are preserved."),
        ["DeleteBackups"]=("Також видалити резервні копії цього профілю","Also delete this profile's backups"),
        ["DeleteConfirm"]=("Для підтвердження введіть точну назву профілю:","Type the exact profile name to confirm:"),
        ["DeleteNow"]=("Остаточно видалити","Permanently delete"), ["DeleteSpace"]=("Файли для видалення","Files to delete"),
        ["Data"]=("Локальні дані","Local data"), ["Archived"]=("Показувати архівовані профілі","Show archived profiles"),
        ["Close"]=("Закрити","Close"), ["LaunchHint"]=("Запуск потребує встановленої гри, сумісної Java й підтвердженого акаунта.","Launch requires installed game files, compatible Java and a verified account.")
    };
    public static string Get(string key)
    {
        if (!Texts.TryGetValue(key, out var value)) return key;
        if (Language == "uk") return value.Uk;
        if (Additional.TryGetValue(Language, out var catalog) && catalog.TryGetValue(key, out var translated)) return translated;
        return value.En;
    }
    public static void Apply(string language)
    {
        Language = Languages.FirstOrDefault(item => string.Equals(item.Code, language, StringComparison.OrdinalIgnoreCase))?.Code ?? "uk";
        if (Application.Current is not { } app) return;
        foreach (var key in Texts.Keys) app.Resources["Text." + key] = Get(key);
    }
}
