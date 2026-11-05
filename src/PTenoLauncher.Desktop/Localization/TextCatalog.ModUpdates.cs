namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterModUpdateTexts()
    {
        Texts["UpdatesTitle"] = ("Оновлення та закріплення модів", "Mod updates and version pins");
        Texts["UpdatesHint"] = ("Modrinth: вибір за датою публікації, типово лише release. Preview перевіряє фактичні JAR; невідомі файли зберігаються. Backup не означає перевірений gameplay.", "Modrinth: publication date order, releases by default. Preview checks actual JARs; unknown files are retained. A backup does not imply verified gameplay.");
        Texts["UpdatesCheck"] = ("Перевірити оновлення модів", "Check mod updates");
        Texts["UpdatesPrerelease"] = ("Включити beta та alpha", "Include beta and alpha");
        Texts["UpdatesSelectChange"] = ("Вибрати зміну", "Select change");
        Texts["UpdatesRemove"] = ("Видалити вибраний відомий мод", "Remove selected known mod");
        Texts["UpdatesPreview"] = ("Розрахувати preview", "Calculate preview");
        Texts["UpdatesPin"] = ("Закріпити вибрану версію", "Pin selected version");
        Texts["UpdatesUnpin"] = ("Зняти закріплення", "Unpin selected version");
        Texts["UpdatesAccept"] = ("Підтверджую показані зміни або дію із закріпленням / recovery", "I confirm the displayed changes or pin / recovery action");
        Texts["UpdatesApply"] = ("Застосувати підтверджений план", "Apply confirmed plan");
        Texts["UpdatesHistory"] = ("Історія операцій модів", "Mod operation history");
        Texts["UpdatesRefreshHistory"] = ("Оновити історію", "Refresh history");
        Texts["UpdatesRollback"] = ("Preview відкату", "Preview rollback");
        Texts["UpdatesRecover"] = ("Відновити перервану операцію", "Recover interrupted operation");
        Texts["UpdatesRow"] = ("{0} · {1} → {2} · {3} · Джерело: {4} · нова version ID: {5} · {6} {7}", "{0} · {1} → {2} · {3} · Source: {4} · proposed version ID: {5} · {6} {7}");
        Texts["UpdatesLocal"] = ("Локальний файл; провайдера не підтверджено", "Local file; provider unconfirmed");
        Texts["UpdatesPinned"] = ("Закріплено", "Pinned");
        Texts["UpdatesRollbackHint"] = ("Перевірено backup та поточний стан. Відкат відновить файли, inventory та pins; потрібне підтвердження.", "Backup and current state verified. Rollback restores files, inventory and pins; confirmation is required.");
        Texts["UpdatesPlanHint"] = ("План фіксує точні IDs і хеші. Зміна профілю, файлів або pins вимагає нового preview.", "The plan fixes exact IDs and hashes. Profile, file or pin changes require a new preview.");
        Texts["UpdatesScan"] = ("Файлів: {0}. Метадані: {1}. Найдавніше отримання: {2}.", "Files: {0}. Metadata: {1}. Oldest fetch: {2}.");
        Texts["UpdatesCached"] = ("кеш; актуальність обмежена датою отримання", "cached; freshness limited by fetch date");
        Texts["UpdatesOnline"] = ("отримано від провайдера", "fetched from provider");
        Texts["UpdatesChecked"] = ("Перевірку завершено. Перегляньте кожен файл і виберіть зміни.", "Check complete. Review each file and select changes.");
        Texts["UpdatesPreviewReady"] = ("План готовий до перегляду та явного підтвердження.", "Plan ready for review and explicit confirmation.");
        Texts["UpdatesPinSaved"] = ("Закріплення збережено. Перевірте оновлення знову.", "Pin saved. Check updates again.");
        Texts["UpdatesApplied"] = ("Операцію завершено; backup збережено. Gameplay не перевірявся.", "Operation committed; backup retained. Gameplay was not tested.");
        Texts["UpdatesRecovered"] = ("Відновлення завершено; перевірте стан профілю.", "Recovery complete; check the profile state.");
        Texts["UpdatesUpdate"] = ("Оновлення", "Update");
        Texts["UpdatesCommitted"] = ("Завершено", "Committed");
        Texts["UpdatesPrepared"] = ("Потребує recovery", "Recovery required");
        Texts["UpdatesDependencyAdd"] = ("Додати залежність", "Add dependency");
        Texts["UpdatesDependencyReplace"] = ("Замінити залежність", "Replace dependency");
        Texts["UpdatesRestore"] = ("Відновити файл", "Restore file");
        Texts["UpdatesRecovery"] = ("Спочатку відновіть незавершену операцію модів.", "Recover the unfinished mod operation first.");
        Texts["UpdatesIntegrity"] = ("Цілісність або належність файлів не підтверджено. Файли користувача збережено.", "File integrity or ownership is unconfirmed. User files are preserved.");
        Texts["UpdatesStale"] = ("Стан змінився або план непридатний. Розрахуйте новий preview; новіші файли не перезаписано.", "State changed or plan is invalid. Calculate a new preview; newer files were not overwritten.");
        Texts["UpdatesPinConflict"] = ("Зміна суперечить точному закріпленню версії.", "Change conflicts with an exact version pin.");
        Texts["UpdatesConfirm"] = ("Потрібне явне підтвердження показаної дії.", "Explicit confirmation of the displayed action is required.");
        Texts["UpdatesUnknown"] = ("Невідомо: провайдера не підтверджено хешем. Автоматична заміна недоступна.", "Unknown: provider not confirmed by hash. Automatic replacement unavailable.");
        Texts["UpdatesUnavailable"] = ("Дані оновлень недоступні або неповні. Це не означає відсутність оновлень.", "Update data is unavailable or incomplete. This does not mean there are no updates.");
        Texts["UpdatesSelect"] = ("Виберіть файл, доступну зміну або операцію з історії.", "Select a file, available change or history operation.");
        Texts["UpdatesBackup"] = ("Backup відсутній, неповний або пошкоджений. Відкат не виконано.", "Backup missing, incomplete or corrupt. Rollback was not performed.");
        Texts["UpdatesCompatibility"] = ("Сумісність результуючого набору не підтверджено. Перегляньте блокери.", "Resulting set compatibility is unconfirmed. Review the blockers.");
        Texts["UpdatesNoNewer"] = ("Немає новішої версії за вибраною політикою в отриманих метаданих.", "No newer version under the selected policy in the fetched metadata.");
        Texts["Readiness.mods.pin.changed"] = ("Точний закріплений файл змінено або втрачено. Потрібне явне розв’язання конфлікту.", "An exact pinned file changed or is missing. Explicit conflict resolution is required.");
    }
}
