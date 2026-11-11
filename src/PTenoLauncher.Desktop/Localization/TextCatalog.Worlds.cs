namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    private static void RegisterWorldTexts()
    {
        Texts["ResourceEnable"] = ("Увімкнути ресурс-пак", "Enable resource pack");
        Texts["ResourceDisable"] = ("Вимкнути ресурс-пак", "Disable resource pack");
        Texts["ResourceEnabled"] = ("Ресурс-пак увімкнено", "Resource pack enabled");
        Texts["ResourceDisabled"] = ("Ресурс-пак вимкнено", "Resource pack disabled");
        Texts["ResourceFormatMatches"] = ("Формат відповідає версії Minecraft", "Format matches this Minecraft version");
        Texts["ResourceFormatMismatch"] = ("Формат не відповідає версії Minecraft", "Format does not match this Minecraft version");
        Texts["ResourceFormatUnknown"] = ("Сумісність формату невідома", "Format compatibility is unknown");
        Texts["ResourceOptionsInvalid"] = ("Не вдалося прочитати вибір ресурс-паків Minecraft.", "Minecraft’s resource pack selection could not be read.");
        Texts["ShaderNeedsLoader"] = ("Для шейдерів потрібен профіль із сумісним завантажувачем і модом шейдерів.", "Shaders need a profile with a compatible loader and shader mod.");
        Texts["ShaderMissingComponent"] = ("Сумісний мод шейдерів не знайдено серед увімкнених модів.", "A compatible shader mod was not found among the enabled mods.");
        Texts["ShaderMissingSuggested"] = ("Додайте {0}, щоб увімкнути шейдери в цьому профілі.", "Add {0} to enable shaders in this profile.");
        Texts["WorkspaceCompatibilityUnknown"] = ("Сумісність не підтверджено", "Compatibility is unconfirmed");
        Texts["ShaderFindComponent"] = ("Знайти компонент шейдерів…", "Find shader component…");
        Texts["BackupChooseDirectory"] = ("Тека нових резервних копій…", "New backup folder…");
        Texts["BackupDirectorySaved"] = ("Нові резервні копії: {0}", "New backups: {0}");
        Texts["BackupDirectoryInvalid"] = ("Виберіть окрему теку для резервних копій.", "Choose a separate backup folder.");
        Texts["BackupDirectoryMissing"] = ("Тека резервних копій недоступна.", "The backup folder is unavailable.");
        Texts["WorldRename"] = ("Перейменувати теку світу", "Rename world folder");
        Texts["WorldCopy"] = ("Копіювати в профіль…", "Copy to profile…");
        Texts["WorldImportZip"] = ("Імпорт або відновлення ZIP…", "Import or restore ZIP…");
        Texts["WorldRestoreFolder"] = ("Відновити з теки…", "Restore from folder…");
        Texts["WorldExportZip"] = ("Експорт світу в ZIP…", "Export world to ZIP…");
        Texts["WorldBackupZip"] = ("Резервна копія світу", "Back up world");
        Texts["WorldChooseProfile"] = ("Цільовий профіль", "Destination profile");
        Texts["WorldNoDestination"] = ("Створіть або розархівуйте інший профіль.", "Create or unarchive another profile.");
        Texts["WorldVersion"] = ("Версія світу: {0}", "World version: {0}");
        Texts["WorldVersionUnknown"] = ("невідома", "unknown");
        Texts["WorldCompatibilityUnknown"] = ("Сумісність із цільовим профілем невідома.", "Compatibility with the destination profile is unknown.");
        Texts["WorldVersionMismatch"] = ("Версії відрізняються. Світ може бути несумісним із цільовим профілем.", "Versions differ. The world may be incompatible with the destination profile.");
        Texts["WorldVersionMatches"] = ("Версії Minecraft збігаються. Сумісність модів потребує окремої перевірки.", "Minecraft versions match. Mod compatibility needs a separate check.");
        Texts["WorldExternalSource"] = ("Вибраний файл або тека", "Selected file or folder");
        Texts["WorldDone"] = ("Світ збережено: {0}", "World saved: {0}");
        Texts["WorldNameInvalid"] = ("Введіть допустиму назву теки світу.", "Enter a valid world folder name.");
        Texts["WorldArchiveInvalid"] = ("Виберіть один світ із файлом level.dat.", "Select a single world containing level.dat.");
    }
}
