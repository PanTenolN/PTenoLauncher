namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    private static void RegisterAiTexts()
    {
        Texts["AiTranslateAll"] = ("Перекласти всі імпортовані набори", "Translate all imported namespaces");
        Texts["AiAssistant"] = ("ШІ-асистент", "AI assistant");
        Texts["AiTranslations"] = ("Переклади модів", "Mod translations");
        Texts["AiModel"] = ("OpenAI: ID моделі", "OpenAI model ID");
        Texts["AiKey"] = ("Власний API-ключ OpenAI (DPAPI)", "Your OpenAI API key (DPAPI)");
        Texts["AiDeleteKey"] = ("Видалити API-ключ", "Delete API key");
        Texts["AiInput"] = ("Запитання або вибраний уривок логу", "Question or selected log excerpt");
        Texts["AiPreview"] = ("Текст для надсилання після приховування даних", "Text to send after redaction");
        Texts["AiConsent"] = ("Я перевірив вибрані дані та дозволяю платні запити OpenAI", "I reviewed the selected data and allow paid OpenAI requests");
        Texts["AiAsk"] = ("Запитати асистента", "Ask assistant");
        Texts["AiAnalyze"] = ("Проаналізувати вибраний лог", "Analyze selected log");
        Texts["AiAnswer"] = ("Відповідь ШІ — перевірте перед застосуванням", "AI answer — review before use");
        Texts["AiImport"] = ("Вибрати JAR для перекладу…", "Select translation JARs…");
        Texts["AiLoadDrafts"] = ("Відкрити збережені переклади", "Open saved translations");
        Texts["AiTranslate"] = ("Перекласти відсутні рядки вибраних модів", "Translate missing strings of selected mods");
        Texts["AiEditorLoad"] = ("Відкрити JSON-редактор", "Open JSON editor");
        Texts["AiEditorSave"] = ("Зберегти ручні правки", "Save manual edits");
        Texts["AiExport"] = ("Створити ресурс-пак", "Create resource pack");
        Texts["AiConfigSaved"] = ("Налаштування ШІ збережено; ключ захищено DPAPI.", "AI settings saved; key protected with DPAPI.");
        Texts["AiKeyRemoved"] = ("API-ключ видалено.", "API key removed.");
        Texts["AiDone"] = ("Операцію ШІ завершено. Перевірте результат.", "AI operation completed. Review the result.");
        Texts["AiImported"] = ("Мовні рядки прочитано локально. До OpenAI ще нічого не надіслано.", "Language strings read locally. Nothing has been sent to OpenAI yet.");
        Texts["AiEditsSaved"] = ("Ручні правки збережено; вони не перезаписуються ШІ.", "Manual edits saved; AI will not overwrite them.");
        Texts["AiSelectDraft"] = ("Оберіть набір мовних рядків.", "Select a language namespace.");
        Texts["AiInputLimit"] = ("Вибраний текст перевищує 48000 символів.", "Selected text exceeds 48000 characters.");
        Texts["AiPrivacy"] = ("OpenAI API оплачується окремо. Надсилаються лише підтверджені дані. Автоматичне приховування не гарантує видалення всіх секретів — перевірте попередній перегляд. store=false не гарантує відсутності зберігання провайдером.", "OpenAI API is billed separately. Only confirmed data is sent. Automatic redaction cannot guarantee removal of every secret: review the preview. store=false does not guarantee zero provider retention.");
        Texts["AiDraftHint"] = ("Оберіть збережений профіль і JAR одного чи кількох модів. Читаються лише сучасні assets/*/lang/en_us.json. Імпорт, ручний редактор і готові переклади працюють без ШІ та інтернету. Оригінальні JAR не змінюються.", "Select a saved profile and one or more mod JARs. Only modern assets/*/lang/en_us.json is read. Import, manual editing and saved translations work without AI or internet. Original JARs are not modified.");
        Texts["AiBatchHint"] = ("Надсилаються лише відсутні або змінені англійські рядки всіх імпортованих наборів цієї мови. До 32 рядків на запит; запитів може бути багато. Завершені порції зберігаються. Ручні правки не перезаписуються.", "Only missing or changed English strings of all imported namespaces for this language are sent. Up to 32 strings per request; many billed requests may be made. Completed batches are saved. Manual edits are preserved.");
        Texts["AiPackHint"] = ("Вкажіть числовий pack_format для вашої версії Minecraft. Готовий ZIP створюється в resourcepacks; увімкніть його в грі. Відсутні й неперевірені рядки пропускаються.", "Enter the numeric pack_format for your Minecraft version. The ZIP is created in resourcepacks; enable it in the game. Missing and unreviewed strings are skipped.");
        Texts["AiHint"] = ("ШІ необов’язковий і типово вимкнений. Онлайн-запити потребують власного ключа та підтвердження. Основні функції незалежні від ШІ.", "AI is optional and off by default. Online requests require your own key and confirmation. Core features are independent of AI.");
        Texts["AiEnabledStatus"] = ("ШІ увімкнено. Для онлайн-запитів налаштуйте ключ і модель.", "AI enabled. Configure a key and model for online requests.");
    }
}
