namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterMrpackTexts()
    {
        Texts["PackTitle"] = ("Імпорт модпака Modrinth", "Import Modrinth modpack");
        Texts["PackChoose"] = ("Обрати .mrpack…", "Choose .mrpack…");
        Texts["PackName"] = ("Назва нового профілю", "New profile name");
        Texts["PackPreview"] = ("Розрахувати preview", "Calculate preview");
        Texts["PackImport"] = ("Імпортувати в новий профіль", "Import into new profile");
        Texts["PackConsent"] = ("Переглянув вміст і дозволяю імпорт або відновлення. Моди можуть виконувати код під час гри.", "I reviewed the contents and approve import or recovery. Mods may execute code during gameplay.");
        Texts["PackPins"] = ("Закріпити точні локальні JAR (до 128)", "Pin exact local JARs (up to 128)");
        Texts["PackHint"] = ("Імпорт копіює перевірені файли в новий профіль. Minecraft, Java та точний loader встановлюються окремо. Імпорт не підтверджує сумісність або готовність до гри.", "Import copies verified files into a new profile. Install Minecraft, Java and the exact loader separately. Import does not confirm compatibility or game readiness.");
        Texts["PackSummary"] = ("{0} · модпак {1} · Minecraft {2} · {3} {4} · файлів: {5} · завантажити: {6:0.0} MB", "{0} · pack {1} · Minecraft {2} · {3} {4} · files: {5} · download: {6:0.0} MB");
        Texts["PackFileLine"] = ("{0} · {1} · клієнт: {2}, сервер: {3} · {4} · {5} · {6:0.0} KB", "{0} · {1} · client: {2}, server: {3} · {4} · {5} · {6:0.0} KB");
        Texts["PackSide.required"] = ("обов’язковий", "required");
        Texts["PackSide.optional"] = ("необов’язковий", "optional");
        Texts["PackSide.unsupported"] = ("не підтримується", "unsupported");
        Texts["PackSelected"] = ("обрано", "selected");
        Texts["PackExcluded"] = ("пропущено", "excluded");
        Texts["PackReplaced"] = ("замінюється overrides", "replaced by overrides");
        Texts["PackCached"] = ("наявний локально", "available locally");
        Texts["PackMissing"] = ("потрібне завантаження", "download needed");
        Texts["PackPending"] = ("Незавершених імпортів: {0}", "Pending imports: {0}");
        Texts["PackRecover"] = ("Відновити перерваний імпорт", "Recover interrupted import");
        Texts["PackPreviewReady"] = ("Preview готовий. Перевірте перелік і підтвердьте імпорт.", "Preview ready. Review the list and confirm import.");
        Texts["PackBlocked"] = ("Імпорт заблоковано. Перевірте причини в preview.", "Import blocked. Review the preview findings.");
        Texts["PackImporting"] = ("Завантаження, перевірка та підготовка нового профілю…", "Downloading, verifying and staging the new profile…");
        Texts["PackImported"] = ("Вміст імпортовано в новий профіль. Далі встановіть потрібні компоненти й перевірте готовність. Gameplay не перевірено.", "Contents imported into a new profile. Install required components and check readiness next. Gameplay is unverified.");
        Texts["PackRecovered"] = ("Відновлення завершено. Незавершена підготовка залишена для діагностики; перевірений профіль опубліковано, якщо він був готовий.", "Recovery finished. Incomplete staging is retained for diagnostics; a verified profile was published if ready.");
        Texts["PackInvalid"] = ("Пошкоджений, небезпечний або неоднозначний .mrpack чи журнал.", "Corrupt, unsafe or ambiguous .mrpack or journal.");
        Texts["PackUnsupported"] = ("Цей формат, компонент або поєднання loaders не підтримується.", "This format, component or loader combination is unsupported.");
        Texts["PackPath"] = ("Небезпечний шлях, дублікат або колізія файлів.", "Unsafe path, duplicate or file collision.");
        Texts["PackBudget"] = ("Перевищено дозволений розмір, кількість файлів або доступне місце.", "Size, file-count or free-space limit exceeded.");
        Texts["PackOrigin"] = ("Джерело завантаження цього файла не підтримується.", "This file’s download source is unsupported.");
        Texts["PackOfflineMissing"] = ("Офлайн бракує перевіреного файла в кеші.", "A verified cached file is missing offline.");
        Texts["PackChanged"] = ("Архів або вибір змінилися. Розрахуйте preview й підтвердьте знову.", "Archive or selection changed. Recalculate the preview and confirm again.");
        Texts["PackIntegrity"] = ("Розмір або контрольні хеші файла не збігаються.", "File size or required hashes do not match.");
        Texts["PackDownload"] = ("Не вдалося отримати файл із дозволених джерел.", "Could not obtain the file from allowed sources.");
        Texts["PackBusy"] = ("Імпорт уже виконується або профіль зайнятий.", "An import is running or the profile is busy.");
        Texts["PackConflict"] = ("Стан імпорту змінено. Новіші або чужі файли не замінено.", "Import state changed. Newer or foreign files were not replaced.");
        Texts["PackRecovery"] = ("Є перерваний імпорт. Підтвердьте відновлення перед новою спробою.", "An interrupted import needs confirmed recovery before retrying.");
        Texts["PackCompatibility"] = ("Статична перевірка JAR виявила залежності або конфлікти, що блокують імпорт.", "Static JAR review found dependencies or conflicts that block import.");
        Texts["PackTarget"] = ("Потрібні точні Minecraft і loader із модпака. Компоненти не встановлено або вибір не збігається.", "The pack requires its exact Minecraft and loader. Components are missing or the selection differs.");
        Texts["Readiness.modpack.target.mismatch"] = ("Потрібні точні Minecraft і loader із модпака. Компоненти не встановлено або вибір не збігається.", "The pack requires its exact Minecraft and loader. Components are missing or the selection differs.");
        Texts["Readiness.modpack.pending"] = ("Є перерваний імпорт. Підтвердьте відновлення перед новою спробою.", "An interrupted import needs confirmed recovery before retrying.");
        Texts["Readiness.modpack.metadata.unknown"] = ("Пошкоджений, небезпечний або неоднозначний .mrpack чи журнал.", "Corrupt, unsafe or ambiguous .mrpack or journal.");
    }
}
