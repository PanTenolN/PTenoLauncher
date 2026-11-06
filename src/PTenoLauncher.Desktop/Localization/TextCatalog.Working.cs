namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    private static void RegisterWorkingTexts()
    {
        Texts.Add("WorkingMark", ("Позначити як робочу", "Mark as working"));
        Texts.Add("WorkingReview", ("Переглянути відновлення…", "Review restoration…"));
        Texts.Add("WorkingSaved", ("Робочу конфігурацію збережено {0:g}", "Working configuration saved {0:g}"));
        Texts.Add("WorkingNone", ("Робочої конфігурації ще немає.", "No working configuration saved yet."));
        Texts.Add("WorkingRestored", ("Робочу конфігурацію відновлено.", "Working configuration restored."));
        Texts.Add("WorkingGameMissing", ("Бракує точних файлів гри, або вони пошкоджені. Відновіть ці файли в кеші лаунчера.", "Exact game files are missing or corrupt. Restore the listed files to the launcher cache."));
        Texts.Add("WorkingModsMissing", ("Бракує точних файлів модів. Додайте їх вручну або знайдіть у перевіреному джерелі.", "Exact mod files are missing. Supply them locally or find them at a verified source."));
        Texts.Add("WorkingOverrides", ("Локальні налаштування залежностей Quilt змінилися. Відновлення призупинено.", "Local Quilt dependency overrides changed. Restoration is blocked."));
        Texts.Add("WorkingCase", ("Назви відрізняються регістром літер.", "The filename capitalization differs."));
        Texts.Add("WorkingInvalidJar", ("Не вдалося прочитати встановлений JAR.", "The installed JAR could not be read."));
        Texts.Add("WorkingExactUnavailable", ("Точний файл недоступний у підтвердженому каталозі.", "The exact file is unavailable in the verified catalog."));
        Texts.Add("WorkingCurrent", ("Поточна конфігурація — {0}", "Current configuration — {0}"));
        Texts.Add("WorkingTarget", ("Збережена конфігурація — {0}", "Saved configuration — {0}"));
        Texts.Add("WorkingAddFile", ("Додати точний файл…", "Supply exact file…"));
        Texts.Add("WorkingFetchExact", ("Знайти точний файл на Modrinth", "Find exact file on Modrinth"));
        Texts.Add("WorkingRemove", ("Буде прибрано {0}", "Remove {0}"));
        Texts.Add("WorkingRestore", ("Буде відновлено {0}", "Restore {0}"));
        Texts.Add("WorkingEnabled", ("увімкнений", "enabled"));
        Texts.Add("WorkingDisabled", ("вимкнений", "disabled"));
        Texts.Add("WorkingNoChanges", ("Профіль уже відповідає збереженій конфігурації.", "This profile already matches the saved configuration."));
        Texts.Add("WorkingApply", ("Підтвердити відновлення", "Confirm restoration"));
    }
}
