namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterTranslationToolsTexts()
    {
        Texts["AiUxAllDirections"] = ("Усі напрямки", "All directions");
        Texts["AiUxDirectionFilter"] = ("Мовний напрямок", "Language direction");
        Texts["AiUxRowSearch"] = ("Пошук в оригіналі, перекладі або ключі", "Search original, translation or key");
        Texts["AiUxMissingFilter"] = ("Без перекладу", "Missing translation");
        Texts["AiUxTrialRow"] = ("Проба", "Trial");
        Texts["AiUxTrialHint"] = ("Спочатку позначте кілька відсутніх рядків для проби. Нічого не надсилається до preview, згоди й натискання кнопки. Інші ресурси книги можуть не підтримуватися.", "First select a few missing rows for a trial. Nothing is sent before preview, consent and pressing the button. Other book resources may be unsupported.");
        Texts["AiUxSelectSample"] = ("Позначити до 5 видимих рядків", "Select up to 5 visible rows");
        Texts["AiUxPreviewTrial"] = ("Переглянути вибраний запит", "Preview selected request");
        Texts["AiUxRunTrial"] = ("Перекласти тільки вибрані рядки", "Translate selected rows only");
        Texts["AiUxTrialScope"] = ("Лише {0} рядків: {1} → {2}. Модель: {3}.", "Only {0} rows: {1} → {2}. Model: {3}.");
        Texts["AiUxTrialLimit"] = ("Оберіть 1–32 відсутні рядки, разом до 10 000 символів. Уже перекладені рядки не перезаписуються.", "Select 1–32 missing rows, up to 10,000 characters total. Existing translations are preserved.");
        Texts["AiUxDeleteEntry"] = ("Видалити цей збережений переклад", "Delete this saved translation");
        Texts["AiUxDeleteScope"] = ("Буде видалено лише чернетку {0}: {1} → {2}, {3} байт. Створений ZIP залишається незмінним: {4}. Він може містити старий переклад, доки ви його вручну не вимкнете або не перегенеруєте. Моди, світи, інші переклади й резервні копії не видаляються.", "Only draft {0}: {1} → {2}, {3} bytes will be deleted. Generated ZIP remains unchanged: {4}. It may contain the old translation until you manually disable or regenerate it. Mods, worlds, other translations and backups are preserved.");
        Texts["AiUxDeleteConfirm"] = ("Підтверджую видалення саме цієї чернетки", "Confirm deletion of this exact draft");
        Texts["AiUxDeleted"] = ("Чернетку видалено; ресурс-пак збережено.", "Draft deleted; resource pack preserved.");
        Texts["AiUxLocalProfile"] = ("Створити профіль тільки для перекладів", "Create a translation-only profile");
        Texts["AiUxLocalProfileHint"] = ("Вкажіть точну версію Minecraft вашої збірки. Вхід і встановлення гри не потрібні. Це лише окрема локальна тека перекладів; інший лаунчер не змінюється.", "Enter the exact Minecraft version of your pack. No login or game installation is required. This is a separate local translation folder; the other launcher is unchanged.");
        Texts["AiUxLocalProfileCreated"] = ("Локальний профіль перекладів створено; гра не встановлювалася.", "Local translation profile created; no game installed.");
        Texts["AiUxExactVersion"] = ("Потрібна точна версія Minecraft вашої збірки, без latest.", "Enter your pack’s exact Minecraft version, not latest.");
        Texts["AiUxClientMetadata"] = ("Підтвердити формат із локального Minecraft client JAR…", "Read format from a local Minecraft client JAR…");
        Texts["AiUxMetadataImported"] = ("Перевірено локальні метадані: {0}. Це не перевірка встановлення або запуску гри.", "Local metadata checked: {0}. This does not verify game installation or gameplay.");
        Texts["AiUxResourceFound"] = ("{0} · {1}: {2} рядків у підтримуваному мовному JSON", "{0} · {1}: {2} strings in supported language JSON");
        Texts["AiUxUnsupportedResources"] = ("Пропущено ресурсів: {0}. Книги, legacy, вкладені архіви або складний JSON цим перекладачем не обробляються. Перші шляхи:\n{1}", "Skipped resources: {0}. Books, legacy, nested archives or complex JSON are not translated by this adapter. First paths:\n{1}");
        Texts["AiUxFullTranslation"] = ("Полний переклад відсутніх рядків — окрема згода", "All missing rows — separate consent");
    }
}
