namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    private static void RegisterPackExportTexts()
    {
        Texts["ExportPack"] = ("Експортувати збірку", "Export modpack");
        Texts["ExportSave"] = ("Підтвердити й зберегти", "Confirm and save");
        Texts["ExportNameLabel"] = ("Назва збірки для одержувача", "Pack name for the recipient");
        Texts["ExportGraphicsChoice"] = ("Поділитися перевіреними параметрами графіки Sodium", "Share verified Sodium graphics options");
        Texts["ExportPolicy"] = ("Для пошуку точних версій Modrinth отримує SHA-512 модів, коли мережа доступна. До архіву входять лише файли зі списку. Світи, акаунти, журнали та приватні налаштування пропускаються.", "When online, Modrinth receives mod SHA-512 hashes to identify exact versions. Only listed files go into the archive. Worlds, accounts, logs and private settings are excluded.");
        Texts["ExportReview"] = ("Підготуйте перегляд файлів перед збереженням.", "Prepare the file preview before saving.");
        Texts["ExportReady"] = ("Перевірте точні версії та рішення для кожного файла.", "Review the exact versions and the decision for each file.");
        Texts["ExportWorking"] = ("Перевіряємо файли збірки…", "Checking modpack files…");
        Texts["ExportDone"] = ("Архів збірки збережено.", "Modpack archive saved.");
        Texts["ExportFailed"] = ("Не вдалося підготувати або зберегти архів.", "Could not prepare or save the archive.");
        Texts["ExportName"] = ("Введіть назву збірки від 1 до 80 символів.", "Enter a pack name of 1 to 80 characters.");
        Texts["ExportTarget"] = ("Спочатку виберіть точну версію завантажувача, яка відповідає профілю.", "Select an exact loader version matching the profile first.");
        Texts["ExportChanged"] = ("Дані профілю змінилися. Перевірте оновлений перегляд і підтвердьте ще раз.", "The source profile changed. Review the updated preview and confirm again.");
        Texts["ExportConsent"] = ("Спочатку підтвердьте поточний список файлів.", "Confirm the current file list first.");
        Texts["ExportDestination"] = ("Оберіть локальний файл .mrpack або .zip за межами даних лаунчера.", "Choose a local .mrpack or .zip outside launcher data.");
        Texts["ExportExists"] = ("Такий файл уже існує. Виберіть іншу назву.", "The file already exists. Choose a different name.");
        Texts["ExportActionInclude"] = ("Включити", "Include");
        Texts["ExportActionLink"] = ("Додати точне посилання", "Add exact link");
        Texts["ExportActionSkip"] = ("Пропустити", "Skip");
        Texts["ExportDisabled"] = ("Мод вимкнено.", "The mod is disabled.");
        Texts["ExportPrivate"] = ("Файл або налаштування поза дозволеним набором.", "The file or setting is outside the allowed set.");
        Texts["ExportGraphicsOff"] = ("Передачу графічних параметрів вимкнено.", "Sharing graphics options is off.");
        Texts["ExportGraphicsChecked"] = ("Збережено лише перевірені перемикачі графіки.", "Only verified graphics switches are included.");
        Texts["ExportUnknownSource"] = ("Джерело не підтверджено. Одержувач має додати файл самостійно.", "Source unverified. The recipient must add the file manually.");
        Texts["ExportUnknownLicense"] = ("Ліцензію не підтверджено. Одержувач має додати файл самостійно.", "License unverified. The recipient must add the file manually.");
        Texts["ExportUnusableSource"] = ("Сумісне джерело не підтверджено. Одержувач має додати файл самостійно.", "A compatible source could not be verified. The recipient must add the file manually.");
        Texts["ExportDuplicate"] = ("Такий вміст уже перевірено в іншому файлі.", "The same content is already accounted for.");
        Texts["ExportCc0"] = ("CC0 підтверджено каталогом і точним JAR.", "CC0 confirmed by the catalog and the exact JAR.");
        Texts["ExportCatalogLink"] = ("Файл одержувач завантажить із каталогу за перевіреним посиланням.", "The recipient downloads the file from the catalog using the verified link.");
        Texts["ExportManual"] = ("Додайте вручну після імпорту: {0}", "Add manually after import: {0}");
    }
}
