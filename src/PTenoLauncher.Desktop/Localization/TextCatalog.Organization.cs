namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    private static void RegisterOrganizationTexts()
    {
        Texts["ProfileOrganization"] = ("Група та нотатки", "Group and notes");
        Texts["ProfileGroup"] = ("Група профілів", "Profile group");
        Texts["ProfileAllGroups"] = ("Усі групи", "All groups");
        Texts["ProfileNotes"] = ("Локальні нотатки", "Local notes");
        Texts["ProfileOrganizationSaved"] = ("Групу й нотатки збережено", "Group and notes saved");
        Texts["ProfileOrganizationInvalid"] = ("Перевірте назву групи та нотатки.", "Check the group name and notes.");
        Texts["ProfileScreenshots"] = ("Скриншоти", "Screenshots");
        Texts["ScreenshotsEmpty"] = ("У цьому профілі ще немає скриншотів.", "This profile has no screenshots yet.");
        Texts["ScreenshotSelect"] = ("Оберіть скриншот.", "Select a screenshot.");
        Texts["ScreenshotInvalid"] = ("Не вдалося відкрити цей PNG.", "Could not open this PNG.");
        Texts["StorageUsage"] = ("Використання сховища", "Storage usage");
        Texts["StorageTotal"] = ("Разом за звітом · {0}", "Report total · {0}");
        Texts["StorageBackups"] = ("Резервні копії · {0}", "Backups · {0}");
        Texts["StorageCache"] = ("Спільний кеш · {0}", "Shared cache · {0}");
        Texts["StorageProfile"] = ("{0} · {1}, з них світи · {2}", "{0} · {1}, including worlds · {2}");
        Texts["StorageIncomplete"] = ("Деякі шляхи або записи пропущено.", "Some paths or records were omitted.");
        Texts["StorageCacheFiles"] = ("Файли кешу та профілі", "Cache files and profiles");
        Texts["StorageReferenceHint"] = ("Профілі наведено за записами встановлення. Розміри відповідають файлам на диску.", "Profiles come from installation records. Sizes reflect files on disk.");
    }
}
