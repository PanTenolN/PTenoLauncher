namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterMinecraftImportTexts()
    {
        Texts["CurseForgeAppDirectory"] = ("Імпорт CurseForge App: вибрати папку профілю", "Import CurseForge App: select profile folder");
        Texts["CurseForgeAppPolicy"] = ("Виберіть один локальний профіль із minecraftinstance.json. Підтримуються Vanilla, Forge і Fabric з точними версіями. Копіюються дозволені локальні файли.", "Select one local profile with minecraftinstance.json. Vanilla, Forge and Fabric with exact versions are supported. Allowed local files are copied.");
        Texts["CurseForgeAppUnsupported"] = ("Непідтримуваний профіль CurseForge App. Потрібен minecraftinstance.json із точними версіями для Vanilla, Forge або Fabric.", "Unsupported CurseForge App profile. A minecraftinstance.json with exact Vanilla, Forge or Fabric versions is required.");
        Texts["ModrinthAppDatabase"] = ("Імпорт Modrinth App: вибрати app.db", "Import Modrinth App: select app.db");
        Texts["ModrinthAppChoose"] = ("Виберіть конкретний профіль і натисніть «Переглянути».", "Select a specific profile and click Preview.");
        Texts["ModrinthAppProfilesFolder"] = ("Вибрати перенесену папку profiles", "Select a relocated profiles folder");
        Texts["ModrinthAppPolicy"] = ("Створюється окрема збірка з дозволених локальних файлів. Для стандартного розташування використовується profiles поряд із app.db. Акаунти, токени, світи й команди пропускаються.", "Allowed local files go into a separate profile. The default profiles folder is beside app.db. Accounts, tokens, worlds and commands are skipped.");
        Texts["ModrinthAppUnsupported"] = ("Непідтримувана база Modrinth App або немає точних версій вибраного профілю.", "Unsupported Modrinth App database or missing exact versions for the selected profile.");
        Texts["ModrinthAppContent"] = ("Не знайдено безпечну папку вибраного профілю. Укажіть папку profiles із даними Modrinth App.", "The selected profile has no safe content folder. Select the Modrinth App profiles folder.");
        Texts["ModrinthAppClose"] = ("Закрийте Modrinth App, щоб він завершив запис бази, потім повторіть перегляд.", "Close Modrinth App so it finishes writing the database, then retry the preview.");
        Texts["MinecraftImportChooseFile"] = ("Імпорт Minecraft Launcher: обрати файл профілів…", "Import Minecraft Launcher: choose profiles file…");
        Texts["MinecraftImportChoose"] = ("Оберіть конкретний профіль і перерахуйте preview", "Choose a specific profile and recalculate preview");
        Texts["MinecraftImportChooseContent"] = ("Явно обрати теку вмісту цього профілю…", "Explicitly choose this profile’s content directory…");
        Texts["MinecraftImportPolicy"] = ("Лише локальний вміст нового незалежного профілю. Оберіть JSON профілів, запис і його теку. Акаунти, токени, світи, гра, runtime та JVM-параметри не переносяться. Зовнішній gameDir не відкривається автоматично.", "Only local content in a new independent profile. Select profiles JSON, an entry and its directory. Accounts, tokens, worlds, game, runtime and JVM arguments are not transferred. External gameDir is never opened automatically.");
        Texts["MinecraftImportMetadata"] = ("Пошкоджені, неоднозначні або непідтримувані метадані Minecraft Launcher.", "Malformed, ambiguous or unsupported Minecraft Launcher metadata.");
        Texts["MinecraftImportUnsupported"] = ("Ці metadata потребують іншого контракту. Версію/loader не вгадують; latest, custom patches/commands та непідтримувані loader блокуються.", "These metadata require another contract. Version/loader is never guessed; latest, custom patches/commands and unsupported loaders are blocked.");
        Texts["MinecraftImportContent"] = ("Потрібне явне підтвердження точної теки вмісту профілю. Іншу або пов’язану теку не підставляють.", "Explicit selection of the profile’s exact content directory is required. A different or linked directory is never substituted.");
    }
}
